using Interaction;
using Mirror;
using Sim;
using Sim.Audio;
using Sim.Enums;
using UnityEngine;
using UnityEngine.AI;
using Action = Sim.Interactables.Action;

/// <summary>
/// Véhicule conduisible (POC). NetworkBehaviour autonome avec sa propre NetworkIdentity
/// et un NetworkTransformReliable en AUTORITÉ CLIENT (syncDirection ClientToServer) —
/// même modèle que le joueur : le conducteur calcule le mouvement en local et le diffuse.
///
/// Namespace GLOBAL volontairement (cf. CLAUDE.md : la réflexion Mirror n'aime pas les
/// namespaces mixtes sur le wire pour les types réseau).
///
/// Flux entrée/sortie :
///   1. Le joueur survole le véhicule → action "Monter" (IInteractable) → CmdEnterVehicle.
///   2. Serveur : si libre, AssignClientAuthority(conducteur) + driverNetId = sa netId,
///      et PARENTE le joueur sous le siège (côté serveur, pour que le relais NT soit correct).
///   3. Le hook driverNetId parente aussi le joueur sur tous les clients (ride-along visuel)
///      et, pour le conducteur LOCAL, déclenche l'état CharacterDrive (caméra + pose).
///   4. Le conducteur (isOwned) conduit au clavier ; le NT diffuse la position.
///   5. Touche X → CmdExitVehicle : RemoveClientAuthority + driverNetId = 0 + dé-parentage.
///
/// PARENTAGE & RÉSEAU : NetworkTransform synchronise la position LOCALE. Pour que le serveur
/// relaie une position monde cohérente, le parentage est appliqué partout (serveur + clients)
/// via <see cref="ApplyParenting"/>.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class VehicleController : NetworkBehaviour, IInteractable {

    [Header("Anchors")]
    [Tooltip("Place du conducteur : le joueur y est parenté (localPos/localRot remis à zéro).")]
    [SerializeField] private Transform seatAnchor;
    [Tooltip("Cible suivie par la caméra pendant la conduite.")]
    [SerializeField] private Transform cameraAnchor;
    [Tooltip("Point de descente à la sortie (échantillonné sur le NavMesh).")]
    [SerializeField] private Transform exitAnchor;
    [Tooltip("Sièges passagers (hors conducteur). Le nombre utilisable est borné par " +
             "config.passengerCount-1 et par MaxPassengerSlots.")]
    [SerializeField] private Transform[] passengerSeats = new Transform[0];

    [Header("Configuration")]
    [Tooltip("SO décrivant ce véhicule (modèle, capacités, conduite, sons). Source de vérité ; " +
             "si absent, on retombe sur les valeurs par défaut ci-dessous.")]
    [SerializeField] private VehicleConfig config;

    [Header("Driving fallback (si pas de config)")]
    [SerializeField] private float maxSpeed     = 6f;
    [SerializeField] private float reverseSpeed = 3f;
    [SerializeField] private float acceleration = 8f;
    [SerializeField] private float braking      = 12f;
    [Tooltip("Décélération en roue libre (accélérateur relâché, sans freiner). Faible = inertie.")]
    [SerializeField] private float friction      = 1.5f;
    [Tooltip("Vitesse de braquage (deg/s) à pleine vitesse.")]
    [SerializeField] private float turnSpeed    = 90f;

    [Header("Engine audio")]
    [Tooltip("Pitch de la boucle moteur au ralenti et à pleine vitesse.")]
    [SerializeField] private float enginePitchIdle = 0.8f;
    [SerializeField] private float enginePitchMax  = 1.7f;

    [Header("Collision")]
    [Tooltip("Layers bloquant l'avancée (murs, décor). Le véhicule ne les traverse pas.")]
    [SerializeField] private LayerMask obstacleMask = ~0;
    [Tooltip("Demi-extents de la boîte de balayage anti-traversée (m).")]
    [SerializeField] private Vector3 sweepHalfExtents = new Vector3(0.9f, 0.5f, 1.6f);
    [SerializeField] private float interactionRange = 3f;

    [SyncVar(hook = nameof(OnDriverChanged))]
    private uint driverNetId;

    // Sièges passagers : un SyncVar par place (le projet n'utilise pas de collections sync).
    private const int MaxPassengerSlots = 3;
    [SyncVar(hook = nameof(OnPassenger0Changed))] private uint passenger0NetId;
    [SyncVar(hook = nameof(OnPassenger1Changed))] private uint passenger1NetId;
    [SyncVar(hook = nameof(OnPassenger2Changed))] private uint passenger2NetId;

    private Action _enterAction;
    private float  _currentSpeed;
    private AudioSource _engineSource;

    public Transform CameraAnchor => cameraAnchor;
    public Transform ExitAnchor   => exitAnchor;
    public bool      IsOccupied   => driverNetId != 0;
    public VehicleConfig Config   => config;

    // ── Paramètres effectifs (config si présente, sinon valeurs de repli) ────────────
    private float MaxSpeed     => config != null ? config.maxSpeed     : maxSpeed;
    private float ReverseSpeed => config != null ? config.reverseSpeed : reverseSpeed;
    private float Acceleration => config != null ? config.acceleration : acceleration;
    private float Braking      => config != null ? config.braking      : braking;
    private float Friction     => config != null ? config.friction     : friction;
    private float TurnSpeed    => config != null ? config.turnSpeed    : turnSpeed;

    /// <summary>Vitesse normalisée [0..1] (magnitude / maxSpeed). Owner-only (calculée localement) :
    /// utilisée par DriveCamera pour l'effet de vitesse (FOV) et le pitch moteur.</summary>
    public float NormalizedSpeed => MaxSpeed > 0f ? Mathf.Clamp01(Mathf.Abs(_currentSpeed) / MaxSpeed) : 0f;

    /// <summary>Vitesse courante en km/h (valeur signée → magnitude). Affichée par le HUD.</summary>
    public float SpeedKmh => Mathf.Abs(_currentSpeed) * 3.6f;

    // ── Sièges passagers ─────────────────────────────────────────────────────────────
    /// <summary>Nombre de places passagers exploitables : min(slots SyncVar, ancres, capacité config-1).</summary>
    private int PassengerCapacity {
        get {
            int byConfig = (config != null ? config.passengerCount : 1) - 1;
            int byAnchors = passengerSeats != null ? passengerSeats.Length : 0;
            return Mathf.Clamp(Mathf.Min(byConfig, byAnchors), 0, MaxPassengerSlots);
        }
    }

    private uint GetPassenger(int i) => i == 0 ? passenger0NetId : i == 1 ? passenger1NetId : passenger2NetId;

    [Server]
    private void SetPassenger(int i, uint v) {
        if (i == 0) passenger0NetId = v;
        else if (i == 1) passenger1NetId = v;
        else passenger2NetId = v;
    }

    private bool IsOccupant(uint id) {
        if (id == 0) return false;
        if (driverNetId == id) return true;
        for (int i = 0; i < MaxPassengerSlots; i++) if (GetPassenger(i) == id) return true;
        return false;
    }

    private bool HasFreeSeat() {
        if (driverNetId == 0) return true;
        for (int i = 0; i < PassengerCapacity; i++) if (GetPassenger(i) == 0) return true;
        return false;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake() {
        Action proto = Resources.Load<Action>("Configurations/Actions/ENTER_VEHICLE");
        if (proto != null) {
            _enterAction = Instantiate(proto);
            _enterAction.OnExecute += OnEnterActionExecuted;
        }

        SetupEngineAudio();
    }

    /// <summary>AudioSource 3D dédiée à la boucle moteur, mutualisée sur le bus SFX du mixer.</summary>
    private void SetupEngineAudio() {
        _engineSource = gameObject.AddComponent<AudioSource>();
        _engineSource.playOnAwake   = false;
        _engineSource.loop          = true;
        _engineSource.spatialBlend  = 1f;
        _engineSource.rolloffMode   = AudioRolloffMode.Linear;
        _engineSource.minDistance   = 3f;
        _engineSource.maxDistance   = 25f;
        _engineSource.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
    }

    private void OnDestroy() {
        if (_enterAction != null) _enterAction.OnExecute -= OnEnterActionExecuted;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────────

    public float GetRange()          => interactionRange;
    public bool  IsInteractable()    => _enterAction != null && HasFreeSeat();
    public bool  IsRightClickOnly()  => false;
    public void  StopInteraction()   { }

    public Action[] GetActions(bool withPriority = false) {
        if (_enterAction == null || !HasFreeSeat()) return System.Array.Empty<Action>();
        return new[] { _enterAction };
    }

    private void OnEnterActionExecuted(Action action) {
        if (PlayerController.Local == null) return;
        CmdEnterVehicle();
    }

    // ── Server: enter / exit ────────────────────────────────────────────────────

    [Command(requiresAuthority = false)]
    private void CmdEnterVehicle(NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        uint id = conn.identity.netId;
        if (IsOccupant(id)) return;                   // déjà à bord

        // Validation de proximité (anti-triche léger).
        float sqr = (conn.identity.transform.position - transform.position).sqrMagnitude;
        if (sqr > (interactionRange * 2f) * (interactionRange * 2f)) return;

        PlayerController pc = conn.identity.GetComponent<PlayerController>();

        // Conducteur si la place est libre, sinon premier siège passager disponible.
        if (driverNetId == 0) {
            netIdentity.AssignClientAuthority(conn);
            ApplyParentingTo(pc, seatAnchor, true);   // serveur
            driverNetId = id;
            return;
        }

        for (int i = 0; i < PassengerCapacity; i++) {
            if (GetPassenger(i) != 0) continue;
            ApplyParentingTo(pc, passengerSeats[i], true); // serveur
            SetPassenger(i, id);
            return;
        }
        // Véhicule plein : aucune action.
    }

    [Command(requiresAuthority = false)]
    private void CmdExitAsPassenger(NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        uint id = conn.identity.netId;
        for (int i = 0; i < MaxPassengerSlots; i++) {
            if (GetPassenger(i) != id) continue;
            if (NetworkServer.spawned.TryGetValue(id, out NetworkIdentity idn) && i < passengerSeats.Length)
                ApplyParentingTo(idn.GetComponent<PlayerController>(), passengerSeats[i], false); // serveur
            SetPassenger(i, 0);
            return;
        }
    }

    /// <summary>Appelé par CharacterPassenger (joueur local) pour descendre du véhicule.</summary>
    public void RequestPassengerExit() => CmdExitAsPassenger();

    [Command]
    private void CmdExitVehicle() {
        if (driverNetId == 0) return;
        ServerReleaseDriver();
    }

    [Server]
    private void ServerReleaseDriver() {
        if (NetworkServer.spawned.TryGetValue(driverNetId, out NetworkIdentity idn)) {
            ApplyParentingTo(idn.GetComponent<PlayerController>(), seatAnchor, false); // serveur
        }
        if (netIdentity.connectionToClient != null) netIdentity.RemoveClientAuthority();
        _currentSpeed = 0f;
        driverNetId = 0;
    }

    // ── SyncVar hook (clients, host inclus) ─────────────────────────────────────

    private void OnDriverChanged(uint previous, uint current) {
        if (current != 0) {
            PlayDoorSound(true);   // portière qui s'ouvre
            StartEngine();
            PlayerController driver = ResolveClientPlayer(current);
            if (driver == null) return;
            ApplyParentingTo(driver, seatAnchor, true);
            if (driver == PlayerController.Local) driver.DriveVehicle(this);
        }
        else if (previous != 0) {
            PlayDoorSound(false);  // portière qui se ferme
            StopEngine();
            PlayerController driver = ResolveClientPlayer(previous);
            if (driver == null) return;
            ApplyParentingTo(driver, seatAnchor, false);
            if (driver == PlayerController.Local) driver.Idle(); // → CharacterDrive.OnExit
        }
    }

    // ── Hooks passagers (clients, host inclus) ──────────────────────────────────────

    private void OnPassenger0Changed(uint prev, uint cur) => OnPassengerSeatChanged(0, prev, cur);
    private void OnPassenger1Changed(uint prev, uint cur) => OnPassengerSeatChanged(1, prev, cur);
    private void OnPassenger2Changed(uint prev, uint cur) => OnPassengerSeatChanged(2, prev, cur);

    private void OnPassengerSeatChanged(int seat, uint prev, uint cur) {
        if (passengerSeats == null || seat >= passengerSeats.Length) return;
        Transform anchor = passengerSeats[seat];
        if (cur != 0) {
            PlayDoorSound(true);
            PlayerController p = ResolveClientPlayer(cur);
            if (p == null) return;
            ApplyParentingTo(p, anchor, true);
            if (p == PlayerController.Local) p.RidePassenger(this);
        }
        else if (prev != 0) {
            PlayDoorSound(false);
            PlayerController p = ResolveClientPlayer(prev);
            if (p == null) return;
            ApplyParentingTo(p, anchor, false);
            if (p == PlayerController.Local) p.Idle(); // → CharacterPassenger.OnExit
        }
    }

    // ── Audio (tous clients) ────────────────────────────────────────────────────────

    private void PlayDoorSound(bool opening) {
        AudioClip clip = config != null ? (opening ? config.doorOpen : config.doorClose) : null;
        if (clip != null) AudioManager.Instance.PlayClip3D(clip, transform.position);
    }

    private void StartEngine() {
        AudioClip loop = config != null ? config.engineLoop : null;
        if (loop == null || _engineSource == null) return;
        _engineSource.clip = loop;
        _engineSource.pitch = enginePitchIdle;
        _engineSource.Play();
    }

    private void StopEngine() {
        if (_engineSource != null) _engineSource.Stop();
    }

    /// <summary>Pitch moteur suivant la vitesse. Le conducteur (owner) a _currentSpeed réel ;
    /// les clients distants restent au pitch ralenti (acceptable pour le POC).</summary>
    private void UpdateEngineAudio() {
        if (_engineSource == null || !_engineSource.isPlaying) return;
        _engineSource.pitch = Mathf.Lerp(enginePitchIdle, enginePitchMax, NormalizedSpeed);
    }

    // ── Klaxon (réseau) ─────────────────────────────────────────────────────────────

    [Command]
    private void CmdHorn() => RpcHorn();

    [ClientRpc]
    private void RpcHorn() {
        if (config != null && config.horn != null)
            AudioManager.Instance.PlayClip3D(config.horn, transform.position);
    }

    private static PlayerController ResolveClientPlayer(uint id) {
        if (NetworkClient.spawned.TryGetValue(id, out NetworkIdentity idn))
            return idn.GetComponent<PlayerController>();
        return null;
    }

    // ── Parentage partagé (serveur + clients) ───────────────────────────────────

    /// <summary>
    /// Parente (ou dé-parente) le joueur sous un siège donné (conducteur ou passager). Doit être
    /// appliqué identiquement partout pour que le NetworkTransform (position locale) reste cohérent.
    /// Idempotent : ré-appliquer le même parent est sans effet.
    /// </summary>
    private void ApplyParentingTo(PlayerController player, Transform seat, bool attach) {
        if (player == null || seat == null) return;
        if (attach) {
            player.transform.SetParent(seat, worldPositionStays: false);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
        }
        else {
            if (player.transform.parent == seat)
                player.transform.SetParent(null, worldPositionStays: true);
        }
    }

    // ── Driving (owner only) ─────────────────────────────────────────────────────

    private void Update() {
        if (isServer) ServerWatchdog();
        UpdateEngineAudio();          // tous clients (pitch moteur)
        if (!isOwned)  return;

        if (Input.GetKeyDown(KeyCode.X)) {
            CmdExitVehicle();
            return;
        }

        if (Input.GetKeyDown(KeyCode.H)) CmdHorn();

        DriveStep();
    }

    private void DriveStep() {
        float throttle = Input.GetAxisRaw("Vertical");   // W/S (ou flèches)
        float steer    = Input.GetAxisRaw("Horizontal"); // A/D
        bool  braking_ = Input.GetKey(KeyCode.Space);    // frein explicite

        // Frein actif : feedback sonore au déclenchement si on roulait.
        if (Input.GetKeyDown(KeyCode.Space) && Mathf.Abs(_currentSpeed) > 1f
            && config != null && config.brake != null) {
            AudioManager.Instance.PlayClip3D(config.brake, transform.position);
        }

        float targetSpeed = throttle > 0f ? throttle * MaxSpeed
                          : throttle < 0f ? throttle * ReverseSpeed
                          : 0f;
        if (braking_) targetSpeed = 0f;                  // le frein l'emporte sur l'accélérateur

        // Frein > freinage actif (sens inverse) > accélération (on pousse) > roue libre (inertie).
        float rate = braking_ ? Braking
                   : Mathf.Approximately(throttle, 0f) ? Friction
                   : Mathf.Abs(targetSpeed) > Mathf.Abs(_currentSpeed) ? Acceleration
                   : Braking;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * Time.deltaTime);

        // Braquage proportionnel à la vitesse (et à son signe, comme une vraie voiture).
        if (Mathf.Abs(_currentSpeed) > 0.05f) {
            float dir = Mathf.Sign(_currentSpeed);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(_currentSpeed) / MaxSpeed);
            transform.Rotate(0f, steer * TurnSpeed * speedFactor * dir * Time.deltaTime, 0f);
        }

        float delta = _currentSpeed * Time.deltaTime;
        if (Mathf.Approximately(delta, 0f)) return;

        Vector3 moveDir = delta >= 0f ? transform.forward : -transform.forward;
        float   dist    = Mathf.Abs(delta);

        // Anti-traversée des murs : balayage en boîte vers l'avant (ignore self).
        if (Physics.BoxCast(transform.position, sweepHalfExtents, moveDir, out RaycastHit hit,
                            transform.rotation, dist + 0.1f, obstacleMask,
                            QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform)) {
            _currentSpeed = 0f;
            return;
        }

        transform.position += moveDir * dist;
    }

    [Server]
    private void ServerWatchdog() {
        // Si le conducteur s'est déconnecté (identity disparue), libère le véhicule.
        if (driverNetId != 0 && !NetworkServer.spawned.ContainsKey(driverNetId)) {
            _currentSpeed = 0f;
            driverNetId = 0;
        }
        // Idem pour les passagers déconnectés.
        for (int i = 0; i < MaxPassengerSlots; i++) {
            uint id = GetPassenger(i);
            if (id != 0 && !NetworkServer.spawned.ContainsKey(id)) SetPassenger(i, 0);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Position de descente échantillonnée sur le NavMesh près de l'exitAnchor.</summary>
    public Vector3 GetExitPosition() {
        Vector3 candidate = exitAnchor != null ? exitAnchor.position
                                               : transform.position + transform.right * 2f;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            return hit.position;
        return candidate;
    }
}
