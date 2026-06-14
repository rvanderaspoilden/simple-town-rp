using System.Collections.Generic;
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

    [Header("Ground / relief")]
    [Tooltip("Layers du sol/terrain à suivre (relief). Si vide, le layer « Ground » est utilisé.")]
    [SerializeField] private LayerMask groundMask;
    [Tooltip("Hauteur de départ du raycast sol au-dessus du pivot (m).")]
    [SerializeField] private float groundRayUp = 1.5f;
    [Tooltip("Portée du raycast sol sous le pivot (m).")]
    [SerializeField] private float groundRayDown = 4f;
    [Tooltip("Décalage vertical du pivot au-dessus du point de contact (m).")]
    [SerializeField] private float groundOffset = 0f;
    [Tooltip("Aligner l'inclinaison du véhicule sur la pente du sol.")]
    [SerializeField] private bool alignToSlope = true;
    [Tooltip("Vitesse de lissage de l'orientation (sol/pente).")]
    [SerializeField] private float groundAlignSpeed = 10f;

    [Header("Engine audio")]
    [Tooltip("Pitch de la boucle moteur au ralenti et à pleine vitesse.")]
    [SerializeField] private float enginePitchIdle = 0.8f;
    [SerializeField] private float enginePitchMax  = 1.7f;

    [Header("Health / collisions")]
    [Tooltip("Particules émises en continu quand le véhicule est KO (fumée).")]
    [SerializeField] private ParticleSystem koParticles;
    [Tooltip("Vitesse de choc (m/s) en-dessous de laquelle un impact n'inflige aucun dégât.")]
    [SerializeField] private float minImpactSpeed = 2f;
    [Tooltip("Dégâts infligés par (m/s) au-dessus du seuil.")]
    [SerializeField] private float impactDamageFactor = 6f;

    [Header("Collision")]
    [Tooltip("Layers bloquant l'avancée (murs, décor). Le véhicule ne les traverse pas.")]
    [SerializeField] private LayerMask obstacleMask = ~0;
    [Tooltip("Demi-extents de la boîte de balayage anti-traversée (m).")]
    [SerializeField] private Vector3 sweepHalfExtents = new Vector3(0.9f, 0.5f, 1.6f);
    [SerializeField] private float interactionRange = 3f;

    [SyncVar(hook = nameof(OnDriverChanged))]
    private uint driverNetId;

    [SyncVar(hook = nameof(OnHealthChanged))]
    private float health = -1f; // -1 = non initialisé (le serveur le règle à MaxHealth au spawn)

    [SyncVar(hook = nameof(OnLockedChanged))]
    private bool isLocked;

    // Propriétaire PERSISTANT (character id), hydraté depuis la DB au spawn. "" = non possédé.
    [SyncVar]
    private string ownerCharacterId = "";

    // Id DB du véhicule (non vide = véhicule sorti du GARAGE). Sert à l'identifier pour le rangement
    // et à savoir s'il est déjà dehors. Vide pour les véhicules de scène / d'exposition.
    [SyncVar]
    private string vehicleDbId = "";

    // Clé stable + modèle, côté serveur uniquement (assignés par le spawner) pour la persistance.
    private string vehicleKey;
    private bool   _ownershipInitialized;

    // Sièges passagers : un SyncVar par place (le projet n'utilise pas de collections sync).
    private const int MaxPassengerSlots = 3;
    [SyncVar(hook = nameof(OnPassenger0Changed))] private uint passenger0NetId;
    [SyncVar(hook = nameof(OnPassenger1Changed))] private uint passenger1NetId;
    [SyncVar(hook = nameof(OnPassenger2Changed))] private uint passenger2NetId;

    private Action _enterAction;
    private Action _lockAction;
    private Action _unlockAction;
    private int    _collisionMask;     // obstacleMask SANS le layer Item (on ne percute pas les objets ramassables)
    private float  _currentSpeed;
    private AudioSource _engineSource;
    private AudioSource _brakeSource;   // boucle de freinage, coupée dès que le frein est relâché
    private float  _lastImpactTime = -10f;

    public Transform CameraAnchor => cameraAnchor;
    public Transform ExitAnchor   => exitAnchor;
    public bool      IsOccupied   => driverNetId != 0;
    public VehicleConfig Config   => config;
    public string    OwnerCharacterId => ownerCharacterId;
    /// <summary>Id DB si ce véhicule a été sorti d'un garage ("" sinon). Identifie un véhicule possédé sorti.</summary>
    public string    VehicleDbId  => vehicleDbId;

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

    // ── Santé ────────────────────────────────────────────────────────────────────────
    public float MaxHealth => config != null ? config.maxHealth : 100f;
    /// <summary>Vie [0..1] pour la barre / le HUD. (-1 = pas encore synchronisé → plein.)</summary>
    public float HealthNormalized => health < 0f ? 1f : (MaxHealth > 0f ? Mathf.Clamp01(health / MaxHealth) : 0f);
    /// <summary>Véhicule hors d'usage : ne roule plus, fume.</summary>
    public bool IsKO => health == 0f;

    public override void OnStartServer() {
        health = MaxHealth;
    }

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

        Action lockProto = Resources.Load<Action>("Configurations/Actions/LOCK");
        if (lockProto != null) {
            _lockAction = Instantiate(lockProto);
            _lockAction.OnExecute += OnLockActionExecuted;
        }
        Action unlockProto = Resources.Load<Action>("Configurations/Actions/UNLOCK");
        if (unlockProto != null) {
            _unlockAction = Instantiate(unlockProto);
            _unlockAction.OnExecute += OnUnlockActionExecuted;
        }

        SetupEngineAudio();
        SetKoParticles(false);

        // Collisions avec tout SAUF les items (objets ramassables au sol).
        int itemLayer = LayerMask.NameToLayer("Item");
        _collisionMask = itemLayer >= 0 ? (obstacleMask & ~(1 << itemLayer)) : (int)obstacleMask;

        // Masque sol : défaut sur le layer « Ground » si non renseigné.
        if (groundMask.value == 0) {
            int g = LayerMask.NameToLayer("Ground");
            if (g >= 0) groundMask = 1 << g;
        }
    }

    public override void OnStartClient() {
        // Applique l'état de vie initial (la barre + les particules) sans jouer de son.
        RefreshHealthVisual();
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

        _brakeSource = gameObject.AddComponent<AudioSource>();
        _brakeSource.playOnAwake   = false;
        _brakeSource.loop          = true;     // jouée tant qu'on freine, stoppée au relâché
        _brakeSource.spatialBlend  = 1f;
        _brakeSource.rolloffMode   = AudioRolloffMode.Linear;
        _brakeSource.minDistance   = 3f;
        _brakeSource.maxDistance   = 22f;
        _brakeSource.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
    }

    /// <summary>Démarre/arrête la boucle de freinage selon l'input (coupée dès qu'on ne freine plus).</summary>
    private void SetBrakeSound(bool on) {
        if (_brakeSource == null) return;
        AudioClip clip = config != null ? config.brake : null;
        if (on && clip != null) {
            if (!_brakeSource.isPlaying) { _brakeSource.clip = clip; _brakeSource.Play(); }
        } else if (_brakeSource.isPlaying) {
            _brakeSource.Stop();
        }
    }

    private void OnDestroy() {
        if (_enterAction != null) _enterAction.OnExecute -= OnEnterActionExecuted;
        if (_lockAction != null) _lockAction.OnExecute -= OnLockActionExecuted;
        if (_unlockAction != null) _unlockAction.OnExecute -= OnUnlockActionExecuted;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────────

    public float GetRange()          => interactionRange;
    public bool  IsInteractable()    => CanEnterAction() || OwnerLockActionAvailable();
    public bool  IsRightClickOnly()  => false;
    public void  StopInteraction()   { }

    /// <summary>Action « Monter » disponible : véhicule déverrouillé + place libre.</summary>
    private bool CanEnterAction() => _enterAction != null && !isLocked && HasFreeSeat();

    /// <summary>Le joueur local est le propriétaire ET à l'extérieur → action verrouiller/déverrouiller.</summary>
    private bool OwnerLockActionAvailable() {
        var local = PlayerController.Local;
        if (local == null || string.IsNullOrEmpty(ownerCharacterId)) return false;
        if (local.CharacterData == null || local.CharacterData.Id != ownerCharacterId) return false;
        if (IsOccupant(local.netId)) return false; // depuis l'extérieur uniquement
        return (isLocked ? _unlockAction : _lockAction) != null;
    }

    public bool IsOwnedBy(string characterId) => !string.IsNullOrEmpty(ownerCharacterId) && ownerCharacterId == characterId;

    public Action[] GetActions(bool withPriority = false) {
        var list = new List<Action>(2);
        if (CanEnterAction()) list.Add(_enterAction);
        if (OwnerLockActionAvailable()) list.Add(isLocked ? _unlockAction : _lockAction);
        return list.Count == 0 ? System.Array.Empty<Action>() : list.ToArray();
    }

    private void OnEnterActionExecuted(Action action) {
        if (PlayerController.Local == null) return;
        CmdEnterVehicle();
    }

    private void OnLockActionExecuted(Action action)   => CmdSetLockAsOwner(true);
    private void OnUnlockActionExecuted(Action action) => CmdSetLockAsOwner(false);

    public bool IsLocked => isLocked;

    // ── Server: enter / exit ────────────────────────────────────────────────────

    [Command(requiresAuthority = false)]
    private void CmdEnterVehicle(NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        if (isLocked) return;                         // verrouillé : personne ne monte
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
            ServerClaimOwnership(pc);                  // premier conducteur = propriétaire (persisté)
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
        if (isLocked) return;                         // verrouillé : le passager ne sort pas
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
        if (isLocked) return;                         // verrouillé : on ne sort pas
        ServerReleaseDriver();
    }

    // ── Verrouillage ────────────────────────────────────────────────────────────────

    /// <summary>Bascule par le conducteur (autorité du véhicule) — propriétaire ou non.</summary>
    [Command]
    private void CmdToggleLockAsDriver() {
        isLocked = !isLocked;
    }

    /// <summary>Verrouille/déverrouille par le propriétaire depuis l'extérieur.</summary>
    [Command(requiresAuthority = false)]
    private void CmdSetLockAsOwner(bool locked, NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        PlayerController pc = conn.identity.GetComponent<PlayerController>();
        string charId = pc != null && pc.CharacterData != null ? pc.CharacterData.Id : null;
        if (string.IsNullOrEmpty(charId) || charId != ownerCharacterId) return; // propriétaire uniquement
        float sqr = (conn.identity.transform.position - transform.position).sqrMagnitude;
        if (sqr > (interactionRange * 2f) * (interactionRange * 2f)) return;
        isLocked = locked;
    }

    // ── Propriété persistée (serveur) ───────────────────────────────────────────────

    /// <summary>Hydrate la propriété depuis la DB au spawn (appelé par VehicleSpawner). La clé
    /// stable permet de retrouver le propriétaire à chaque (re)spawn. Pas de position persistée.</summary>
    [Server]
    public void ServerInitOwnership(string key) {
        if (_ownershipInitialized) return;
        _ownershipInitialized = true;
        vehicleKey = key;
        if (string.IsNullOrEmpty(key) || ApiManager.Instance == null) return;
        string mdl = config != null ? config.id : "vehicle"; // on stocke l'id de config dans `model`
        ApiManager.Instance.StartCoroutine(
            ApiManager.Instance.EnsureVehicleCoroutine(key, mdl, row => {
                if (row != null) ownerCharacterId = row.ownerCharacterId ?? "";
            }));
    }

    /// <summary>Initialise un véhicule SORTI d'un garage : propriétaire connu + id DB (pour le rangement).
    /// Pas d'appel DB (la propriété est déjà persistée).</summary>
    [Server]
    public void ServerInitFromGarage(string dbId, string ownerCharId) {
        _ownershipInitialized = true;
        vehicleDbId = dbId ?? "";
        ownerCharacterId = ownerCharId ?? "";
    }

    /// <summary>Le premier conducteur réclame la propriété (persistée), si le véhicule est libre.</summary>
    [Server]
    private void ServerClaimOwnership(PlayerController driver) {
        if (!string.IsNullOrEmpty(ownerCharacterId)) return; // déjà possédé
        string charId = driver != null && driver.CharacterData != null ? driver.CharacterData.Id : null;
        if (string.IsNullOrEmpty(charId)) return;
        ownerCharacterId = charId;
        if (!string.IsNullOrEmpty(vehicleKey) && ApiManager.Instance != null)
            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.SetVehicleOwnerCoroutine(vehicleKey, charId, _ => { }));
    }

    private void OnLockedChanged(bool previous, bool current) {
        AudioClip clip = config != null ? (current ? config.lockSound : config.unlockSound) : null;
        if (clip != null) AudioManager.Instance.PlayClip3D(clip, transform.position);
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
        if (IsKO) return;
        AudioClip loop = config != null ? config.engineLoop : null;
        if (loop == null || _engineSource == null) return;
        _engineSource.clip = loop;
        _engineSource.pitch = enginePitchIdle;
        _engineSource.Play();
    }

    private void StopEngine() {
        if (_engineSource != null) _engineSource.Stop();
        SetBrakeSound(false); // coupe aussi le frein (sortie / KO)
    }

    /// <summary>Pitch moteur suivant la vitesse. Le conducteur (owner) a _currentSpeed réel ;
    /// les clients distants restent au pitch ralenti (acceptable pour le POC).</summary>
    private void UpdateEngineAudio() {
        if (_engineSource == null || !_engineSource.isPlaying) return;
        _engineSource.pitch = Mathf.Lerp(enginePitchIdle, enginePitchMax, NormalizedSpeed);
    }

    // ── Santé / collisions ──────────────────────────────────────────────────────────

    private void OnHealthChanged(float previous, float current) {
        RefreshHealthVisual();

        // Son de choc proportionnel aux dégâts subis (ignore l'init -1 → MaxHealth).
        if (previous >= 0f && current < previous && config != null && config.impact != null) {
            float severity = Mathf.Clamp01((previous - current) / Mathf.Max(1f, MaxHealth) * 3f);
            AudioManager.Instance.PlayClip3D(config.impact, transform.position, 0.4f + 0.6f * severity);
        }

        // Passage KO.
        if (current == 0f && previous != 0f) {
            StopEngine();
            if (config != null && config.ko != null)
                AudioManager.Instance.PlayClip3D(config.ko, transform.position);
        }
    }

    /// <summary>Met à jour les particules KO d'après la vie courante (la barre de vie vit dans le HUD).</summary>
    private void RefreshHealthVisual() {
        SetKoParticles(IsKO);
    }

    private void SetKoParticles(bool on) {
        if (koParticles == null) return;
        if (on) { if (!koParticles.isPlaying) koParticles.Play(); }
        else    { if (koParticles.isPlaying)  koParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
    }

    /// <summary>Applique un impact côté serveur (appelé par le conducteur via Command).</summary>
    [Server]
    private void ServerApplyImpact(float impactSpeed) {
        if (health <= 0f) return;
        float over = impactSpeed - minImpactSpeed;
        if (over <= 0f) return;
        float damage = over * impactDamageFactor;
        health = Mathf.Max(0f, health - damage);
    }

    [Command(requiresAuthority = false)]
    private void CmdReportImpact(float impactSpeed) => ServerApplyImpact(impactSpeed);

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
        if (Input.GetKeyDown(KeyCode.L)) CmdToggleLockAsDriver(); // clé : verrouiller/déverrouiller

        DriveStep();
    }

    private void DriveStep() {
        // KO : le véhicule ne roule plus (la fumée tourne via les particules).
        if (IsKO) { _currentSpeed = 0f; SetBrakeSound(false); return; }

        float throttle = Input.GetAxisRaw("Vertical");   // W/S (ou flèches)
        float steer    = Input.GetAxisRaw("Horizontal"); // A/D
        bool  braking_ = Input.GetKey(KeyCode.Space);    // frein explicite

        // Boucle de freinage : jouée tant qu'on freine ET qu'on roule encore, coupée sinon.
        SetBrakeSound(braking_ && Mathf.Abs(_currentSpeed) > 1f);

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

        // Cap (heading) à plat : on travaille dans le plan horizontal, le relief n'altère pas
        // la vitesse ni le braquage (l'inclinaison est purement visuelle, appliquée après).
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
        flatForward.Normalize();

        // Braquage proportionnel à la vitesse (yaw autour de l'up MONDE → cohérent en pente).
        if (Mathf.Abs(_currentSpeed) > 0.05f) {
            float dir = Mathf.Sign(_currentSpeed);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(_currentSpeed) / MaxSpeed);
            float yaw = steer * TurnSpeed * speedFactor * dir * Time.deltaTime;
            flatForward = Quaternion.AngleAxis(yaw, Vector3.up) * flatForward;
        }

        float delta = _currentSpeed * Time.deltaTime;
        float dist  = Mathf.Abs(delta);

        if (!Mathf.Approximately(delta, 0f)) {
            Vector3 moveDir = delta >= 0f ? flatForward : -flatForward;

            // Anti-traversée des murs : balayage en boîte vers l'avant (ignore self).
            if (Physics.BoxCast(transform.position, sweepHalfExtents, moveDir, out RaycastHit wall,
                                transform.rotation, dist + 0.1f, _collisionMask,
                                QueryTriggerInteraction.Ignore)
                && !wall.collider.transform.IsChildOf(transform)) {
                float impactSpeed = Mathf.Abs(_currentSpeed);
                _currentSpeed = 0f;
                if (impactSpeed > minImpactSpeed && Time.time - _lastImpactTime > 0.4f) {
                    _lastImpactTime = Time.time;
                    CmdReportImpact(impactSpeed);
                }
            } else {
                transform.position += moveDir * dist; // déplacement planaire
            }
        }

        ApplyGroundFollow(flatForward); // colle au sol + épouse la pente
    }

    /// <summary>Cale le véhicule sur le relief : raycast vers le bas pour le Y, et oriente le
    /// véhicule selon le cap (flatForward) + la normale de la pente. Sans sol détecté, reste à plat.</summary>
    private void ApplyGroundFollow(Vector3 flatForward) {
        Vector3 origin = transform.position + Vector3.up * groundRayUp;
        Vector3 up = Vector3.up;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayUp + groundRayDown,
                            groundMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform)) {
            Vector3 p = transform.position;
            p.y = hit.point.y + groundOffset;
            transform.position = p;
            if (alignToSlope) up = hit.normal;
        }

        Vector3 fwdOnPlane = Vector3.ProjectOnPlane(flatForward, up);
        if (fwdOnPlane.sqrMagnitude < 1e-4f) return;
        Quaternion target = Quaternion.LookRotation(fwdOnPlane.normalized, up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, groundAlignSpeed * Time.deltaTime);
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
