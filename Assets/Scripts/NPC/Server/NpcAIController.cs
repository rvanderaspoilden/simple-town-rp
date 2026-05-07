using AI;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// IA serveur d'un NPC. Vit UNIQUEMENT côté serveur.
/// Le GameObject porteur n'a PAS de NetworkIdentity.
///
/// Architecture :
///   - Hôte d'une <see cref="StateMachine"/> (réutilisée du système joueur).
///   - États : NpcIdleState, NpcGoToInterestAreaState, NpcBackToHomeState.
///   - Configuré par <see cref="NpcSpawnManager"/> via <see cref="ConfigureForSpawn"/>
///     juste après instanciation, AVANT que Start ne s'exécute.
///
/// Le prefab NPC est une copie du prefab joueur dont les composants gameplay/réseau
/// ont été retirés mais qui conserve Animator, PlayerAnimator et CharacterStyleSetup.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NpcAIController : MonoBehaviour, ICharacterEntity
{
    // ── ICharacterEntity ──────────────────────────────────────────────────
    public uint      OccupantId => CharacterEntityIds.EncodeNpc(_npcId);
    public bool      IsNpc      => true;
    public Transform Transform  => transform;

    [Header("Identification")]
    [Tooltip("PrefabId tel que référencé dans NpcPrefabDatabase (résolu côté client). " +
             "Peut être surchargé par NpcSpawnManager via ConfigureForSpawn.")]
    [SerializeField]
    private string prefabId = "default";

    [Tooltip("Room dans laquelle ce NPC est diffusé. POC = \"city\".")]
    [SerializeField]
    private string roomId = "city";

    [Header("Style")]
    [Tooltip("Si vrai, randomise le style via CharacterStyleSetup.Randomize() au spawn.")]
    [SerializeField]
    private bool randomizeStyleOnSpawn = true;

    [Header("Wander settings")]
    [SerializeField] private float minIdleSeconds        = 1.5f;
    [SerializeField] private float maxIdleSeconds        = 4f;
    [SerializeField] private float arriveDistance        = 0.5f;

    [Tooltip("Nombre de visites de points d'intérêt avant de rentrer chez soi.")]
    [SerializeField] private int   minVisitsBeforeReturn = 2;
    [SerializeField] private int   maxVisitsBeforeReturn = 5;

    [Header("Sit settings")]
    [Tooltip("Probabilité de tenter de s'asseoir à la fin d'une période d'idle (0..1).")]
    [Range(0f, 1f)]
    [SerializeField] private float sitProbability = 0.3f;
    [SerializeField] private float minSitSeconds  = 5f;
    [SerializeField] private float maxSitSeconds  = 12f;

    [Tooltip("Distance maxi à laquelle un NPC ira chercher un siège (mètres). " +
             "0 = illimité.")]
    [SerializeField] private float maxSeatSearchDistance = 12f;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private NavMeshAgent        _agent;
    private CharacterStyleSetup _styleSetup;
    private StateMachine        _stateMachine;

    private NpcIdleState              _idleState;
    private NpcGoToInterestAreaState  _goToState;
    private NpcBackToHomeState        _backHomeState;
    private NpcSitState               _sitState;

    private int _npcId             = -1;
    private int _visitCount        = 0;
    private int _visitTarget       = 0;

    // Données injectées par le SpawnManager avant le Start.
    private NpcSpawnPoint _home;
    private NpcIdentity   _identity = NpcIdentity.Empty;

    // ── Public API (accédée par les états) ────────────────────────────────────
    public int           NpcId               => _npcId;
    public string        RoomId              => roomId;
    public string        PrefabId            => prefabId;
    public NpcSpawnPoint Home                => _home;
    public Vector3       HomePosition        => _home != null ? _home.Position : transform.position;
    public NpcIdentity   Identity            => _identity;
    public float         MinIdleSeconds      => minIdleSeconds;
    public float         MaxIdleSeconds      => maxIdleSeconds;
    public float         MinSitSeconds          => minSitSeconds;
    public float         MaxSitSeconds          => maxSitSeconds;
    public float         MaxSeatSearchDistance  => maxSeatSearchDistance;

    public void SetAgentEnabled(bool value) {
        if (_agent != null) _agent.enabled = value;
    }

    /// <summary>Mémorise le dernier point d'intérêt visité pour éviter de le re-piquer.</summary>
    public InterestPoint LastVisitedInterest { get; set; }

    /// <summary>
    /// Appelé par <see cref="NpcSpawnManager"/> juste après Instantiate, avant Start.
    /// Permet d'injecter les données de spawn (home, identité, room).
    /// </summary>
    public void ConfigureForSpawn(NpcSpawnPoint home, NpcIdentity identity, string roomId, string prefabId) {
        _home     = home;
        _identity = identity;
        this.roomId   = roomId   ?? this.roomId;
        this.prefabId = !string.IsNullOrEmpty(prefabId) ? prefabId : this.prefabId;
    }

    public void IncrementVisitCount() => _visitCount++;

    public bool ShouldReturnHome() => _visitCount >= _visitTarget;

    public void StopAgent() {
        if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
    }

    public void SetDestination(Vector3 destination) {
        if (_agent != null && _agent.isOnNavMesh) _agent.SetDestination(destination);
    }

    public bool HasReachedDestination() {
        if (_agent == null) return true;
        return !_agent.pathPending
               && _agent.remainingDistance <= arriveDistance
               && (!_agent.hasPath || _agent.velocity.sqrMagnitude < 0.01f);
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake() {
        _agent      = GetComponent<NavMeshAgent>();
        _styleSetup = GetComponent<CharacterStyleSetup>();
    }

    private void Start() {
        if (!NetworkServer.active) {
            // IA STRICTEMENT serveur — sécurité côté host.
            enabled = false;
            return;
        }

        // Style local + sérialisation pour les clients.
        string styleJson = string.Empty;
        if (_styleSetup != null) {
            if (randomizeStyleOnSpawn) _styleSetup.Randomize();
            styleJson = JsonUtility.ToJson(_styleSetup.GetStyle());
        }

        _npcId = NpcServerManager.Instance.Register(
            roomId, prefabId, transform.position, transform.rotation, styleJson, _identity);

        _visitTarget = Random.Range(minVisitsBeforeReturn, maxVisitsBeforeReturn + 1);
        BuildStateMachine();
    }

    private void OnDestroy() {
        if (NetworkServer.active) {
            // Libère tout siège tenu par ce NPC (au cas où il est détruit en pleine assise).
            if (_npcId > 0) SeatService.ReleaseAllSeats(this, roomId);
            if (_npcId > 0) NpcServerManager.Instance.Unregister(_npcId);
            NpcSpawnManager.Instance.OnNpcDestroyed(this);
        }
    }

    // Mémo du state envoyé pour détecter les transitions et déclencher
    // un broadcast immédiat (cf. NpcServerManager.NotifyStateChanged).
    private NpcStateType _lastNotifiedState = (NpcStateType)255;

    private void Update() {
        if (!NetworkServer.active || _stateMachine == null) return;

        _stateMachine.Tick();

        // L'état logique vient directement de la state machine (source unique).
        NpcStateType current = (_stateMachine.CurrentState is NpcStateBase nsb)
            ? nsb.StateType
            : NpcStateType.Idle;

        // Walking est dérivé du couple (Idle, vélocité) pour préserver l'animation
        // "Walk" sur les blend trees existants pendant les déplacements rapides.
        // Les states explicites (Sitting, GoingToInterestArea, BackToHome) priment.
        if (current == NpcStateType.Idle && _agent.velocity.sqrMagnitude > 0.01f) {
            current = NpcStateType.Walking;
        }

        // Détection de transition → notify (broadcast immédiat).
        if (current != _lastNotifiedState) {
            NpcServerManager.Instance.NotifyStateChanged(_npcId, current);
            _lastNotifiedState = current;
        }

        NpcServerManager.Instance.PushTransform(
            _npcId,
            transform.position,
            transform.rotation,
            _agent.velocity,
            current
        );
    }

    // ── State machine wiring ──────────────────────────────────────────────────

    private void BuildStateMachine() {
        _stateMachine  = new StateMachine();
        _idleState     = new NpcIdleState(this);
        _goToState     = new NpcGoToInterestAreaState(this);
        _backHomeState = new NpcBackToHomeState(this);
        _sitState      = new NpcSitState(this);

        // Idle → priorité au retour à la maison ; sinon tirage aléatoire entre Sit et GoTo.
        // Les prédicats consomment un flag _wantsToSit décidé à la sortie d'Idle.
        _stateMachine.AddTransition(_idleState, _backHomeState,
            () => _idleState.IsIdleComplete && ShouldReturnHome());
        _stateMachine.AddTransition(_idleState, _sitState,
            () => _idleState.IsIdleComplete && !ShouldReturnHome() && DecideSit());
        _stateMachine.AddTransition(_idleState, _goToState,
            () => _idleState.IsIdleComplete && !ShouldReturnHome());

        // Visite ou assise terminée → Idle
        _stateMachine.AddTransition(_goToState, _idleState,
            () => _goToState.HasArrived);
        _stateMachine.AddTransition(_sitState, _idleState,
            () => _sitState.HasFinished);

        _stateMachine.SetState(_idleState);
    }

    /// <summary>
    /// Tirage aléatoire (une seule fois par sortie d'Idle) pour décider si le NPC va
    /// tenter de s'asseoir. Le résultat est mémorisé pour rester stable au sein d'une
    /// même évaluation de transition.
    /// </summary>
    private bool DecideSit() {
        // Le prédicat n'est évalué qu'une fois IsIdleComplete vrai, et la transition
        // qui suit (Sit OU GoTo) reset Idle dès le tick courant. Donc en pratique
        // le tirage n'a lieu qu'une fois par cycle Idle.
        return Random.value < sitProbability;
    }
}
