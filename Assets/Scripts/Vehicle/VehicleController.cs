using Interaction;
using Mirror;
using Sim;
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

    [Header("Driving (kinematic arcade)")]
    [SerializeField] private float maxSpeed     = 6f;
    [SerializeField] private float reverseSpeed = 3f;
    [SerializeField] private float acceleration = 8f;
    [SerializeField] private float braking      = 12f;
    [Tooltip("Vitesse de braquage (deg/s) à pleine vitesse.")]
    [SerializeField] private float turnSpeed    = 90f;

    [Header("Collision")]
    [Tooltip("Layers bloquant l'avancée (murs, décor). Le véhicule ne les traverse pas.")]
    [SerializeField] private LayerMask obstacleMask = ~0;
    [Tooltip("Demi-extents de la boîte de balayage anti-traversée (m).")]
    [SerializeField] private Vector3 sweepHalfExtents = new Vector3(0.9f, 0.5f, 1.6f);
    [SerializeField] private float interactionRange = 3f;

    [SyncVar(hook = nameof(OnDriverChanged))]
    private uint driverNetId;

    private Action _enterAction;
    private float  _currentSpeed;

    public Transform CameraAnchor => cameraAnchor;
    public Transform ExitAnchor   => exitAnchor;
    public bool      IsOccupied   => driverNetId != 0;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake() {
        Action proto = Resources.Load<Action>("Configurations/Actions/ENTER_VEHICLE");
        if (proto != null) {
            _enterAction = Instantiate(proto);
            _enterAction.OnExecute += OnEnterActionExecuted;
        }
    }

    private void OnDestroy() {
        if (_enterAction != null) _enterAction.OnExecute -= OnEnterActionExecuted;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────────

    public float GetRange()          => interactionRange;
    public bool  IsInteractable()    => driverNetId == 0 && _enterAction != null;
    public bool  IsRightClickOnly()  => false;
    public void  StopInteraction()   { }

    public Action[] GetActions(bool withPriority = false) {
        if (driverNetId != 0 || _enterAction == null) return System.Array.Empty<Action>();
        return new[] { _enterAction };
    }

    private void OnEnterActionExecuted(Action action) {
        if (PlayerController.Local == null) return;
        CmdEnterVehicle();
    }

    // ── Server: enter / exit ────────────────────────────────────────────────────

    [Command(requiresAuthority = false)]
    private void CmdEnterVehicle(NetworkConnectionToClient conn = null) {
        if (driverNetId != 0) return;                 // déjà occupé
        if (conn?.identity == null) return;

        // Validation de proximité (anti-triche léger).
        float sqr = (conn.identity.transform.position - transform.position).sqrMagnitude;
        if (sqr > (interactionRange * 2f) * (interactionRange * 2f)) return;

        netIdentity.AssignClientAuthority(conn);
        ApplyParenting(conn.identity.GetComponent<PlayerController>(), true); // serveur
        driverNetId = conn.identity.netId;
    }

    [Command]
    private void CmdExitVehicle() {
        if (driverNetId == 0) return;
        ServerReleaseDriver();
    }

    [Server]
    private void ServerReleaseDriver() {
        if (NetworkServer.spawned.TryGetValue(driverNetId, out NetworkIdentity idn)) {
            ApplyParenting(idn.GetComponent<PlayerController>(), false); // serveur
        }
        if (netIdentity.connectionToClient != null) netIdentity.RemoveClientAuthority();
        _currentSpeed = 0f;
        driverNetId = 0;
    }

    // ── SyncVar hook (clients, host inclus) ─────────────────────────────────────

    private void OnDriverChanged(uint previous, uint current) {
        if (current != 0) {
            PlayerController driver = ResolveClientPlayer(current);
            if (driver == null) return;
            ApplyParenting(driver, true);
            if (driver == PlayerController.Local) driver.DriveVehicle(this);
        }
        else if (previous != 0) {
            PlayerController driver = ResolveClientPlayer(previous);
            if (driver == null) return;
            ApplyParenting(driver, false);
            if (driver == PlayerController.Local) driver.Idle(); // → CharacterDrive.OnExit
        }
    }

    private static PlayerController ResolveClientPlayer(uint id) {
        if (NetworkClient.spawned.TryGetValue(id, out NetworkIdentity idn))
            return idn.GetComponent<PlayerController>();
        return null;
    }

    // ── Parentage partagé (serveur + clients) ───────────────────────────────────

    /// <summary>
    /// Parente (ou dé-parente) le joueur sous le siège. Doit être appliqué identiquement
    /// partout pour que le NetworkTransform (position locale) reste cohérent.
    /// Idempotent : ré-appliquer le même parent est sans effet.
    /// </summary>
    private void ApplyParenting(PlayerController player, bool attach) {
        if (player == null) return;
        if (attach) {
            player.transform.SetParent(seatAnchor, worldPositionStays: false);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
        }
        else {
            if (player.transform.parent == seatAnchor)
                player.transform.SetParent(null, worldPositionStays: true);
        }
    }

    // ── Driving (owner only) ─────────────────────────────────────────────────────

    private void Update() {
        if (isServer) ServerWatchdog();
        if (!isOwned)  return;

        if (Input.GetKeyDown(KeyCode.X)) {
            CmdExitVehicle();
            return;
        }

        DriveStep();
    }

    private void DriveStep() {
        float throttle = Input.GetAxisRaw("Vertical");   // W/S (ou flèches)
        float steer    = Input.GetAxisRaw("Horizontal"); // A/D

        float targetSpeed = throttle > 0f ? throttle * maxSpeed
                          : throttle < 0f ? throttle * reverseSpeed
                          : 0f;
        float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(_currentSpeed) ? acceleration : braking;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * Time.deltaTime);

        // Braquage proportionnel à la vitesse (et à son signe, comme une vraie voiture).
        if (Mathf.Abs(_currentSpeed) > 0.05f) {
            float dir = Mathf.Sign(_currentSpeed);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(_currentSpeed) / maxSpeed);
            transform.Rotate(0f, steer * turnSpeed * speedFactor * dir * Time.deltaTime, 0f);
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
