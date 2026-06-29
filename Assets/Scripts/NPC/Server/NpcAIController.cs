using System.Collections.Generic;
using AI;
using Mirror;
using Sim.Logging;
using Sim.NPC;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// IA serveur d'un NPC. Vit UNIQUEMENT côté serveur.
/// Le GameObject porteur n'a PAS de NetworkIdentity.
///
/// Architecture pooling :
///   Le cycle de vie est piloté par OnEnable/OnDisable (et non Start/OnDestroy)
///   pour être compatible avec NpcPool — le même GO est réactivé plusieurs fois.
///
///   Ordre garanti par NpcPool.Get() :
///     1. ConfigureForSpawn()  — injecte home / identity / roomId / config
///     2. ResetForPool()       — efface tout état transient de la vie précédente
///     3. SetActive(true)      — déclenche OnEnable → Register + BuildStateMachine
///
///   NpcPool.Release() appelle SetActive(false) → OnDisable → cleanup propre.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NpcAIController : MonoBehaviour, ICharacterEntity
{
    // ── ICharacterEntity ──────────────────────────────────────────────────
    public uint      OccupantId => CharacterEntityIds.EncodeNpc(_npcId);
    public bool      IsNpc      => true;
    public Transform Transform  => transform;

    [Header("Identification")]
    [Tooltip("Room dans laquelle ce NPC est diffusé. POC = \"city\".")]
    [SerializeField]
    private string roomId = "city";

    [Header("Style")]
    [Tooltip("Si vrai, randomise le style via CharacterStyleSetup.Randomize() au spawn.")]
    [SerializeField]
    private bool randomizeStyleOnSpawn = true;

    [Header("Movement settings")]
    [Tooltip("Vitesse de déplacement de l'agent. Doit rester <= 1.5 pour conserver " +
             "l'animation Walk (au-delà le blend tree passe en Run).")]
    [SerializeField] private float walkSpeed = 1.5f;

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

    // Sous-graphe marchand (instancié seulement si ce NPC est attitré à un stand).
    private NpcMerchantState          _merchantState;
    private NpcMerchantPauseState     _merchantPause;

    // Config NPC re-dérivée à CHAQUE ConfigureForSpawn depuis le home. Ne JAMAIS l'effacer dans
    // ResetForPool : sinon un GO recyclé spawnerait avec la mauvaise config (marchand sur un point
    // non-marchand, ou l'inverse).
    private NpcConfig                 _config;

    private int _npcId       = -1;
    private int _visitCount  = 0;
    private int _visitTarget = 0;

    // Renversement (ragdoll) : pause l'IA jusqu'à ce temps serveur (<0 = pas renversé).
    private float _knockdownUntil = -1f;
    private const float KnockdownDuration = 3f;

    // Freeze d'interaction (overlay transverse, pas un état de la state machine).
    // Le compteur d'owners est la List.Count : tant qu'au moins un joueur tient le lock, on
    // skip _stateMachine.Tick() et on rotate vers l'owner le plus proche. Le service serveur
    // (NpcInteractionService) maintient le mapping conn↔npc et les timeouts, et appelle
    // ServerBeginInteraction / ServerEndInteraction qui poussent/retirent les Transforms ici.
    private readonly List<Transform> _interactionOwners = new List<Transform>(4);
    private const float InteractionRotationSpeedDeg = 360f;
    private const float InteractionFaceMinDistanceSqr = 0.01f; // 0.1m horizontal squared

    // Registre id → contrôleur, pour que le serveur retrouve un NPC à renverser depuis son npcId
    // (les NPC n'ont pas de NetworkIdentity).
    private static readonly Dictionary<int, NpcAIController> _byId = new Dictionary<int, NpcAIController>();
    public static bool TryGet(int npcId, out NpcAIController controller) => _byId.TryGetValue(npcId, out controller);

    // Données injectées par NpcPool.Get() via ConfigureForSpawn AVANT OnEnable.
    private NpcSpawnPoint _home;
    private NpcIdentity   _identity = NpcIdentity.Empty;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Prefab source utilisé par NpcPool pour identifier la queue de recyclage.</summary>
    public GameObject SourcePrefab { get; set; }

    public int           NpcId               => _npcId;
    public string        RoomId              => roomId;
    public NpcSpawnPoint Home                => _home;
    public Vector3       HomePosition        => _home != null ? _home.Position : transform.position;
    public NpcIdentity   Identity            => _identity;
    public float         MinIdleSeconds      => minIdleSeconds;
    public float         MaxIdleSeconds      => maxIdleSeconds;
    public float         MinSitSeconds       => minSitSeconds;
    public float         MaxSitSeconds       => maxSitSeconds;
    public float         MaxSeatSearchDistance => maxSeatSearchDistance;

    public NpcConfig         Config     => _config;
    public string            ConfigId   => _config != null ? _config.Id : null;
    public MerchantNpcConfig Merchant   => _config as MerchantNpcConfig;
    public bool              IsMerchant => _config != null && _config.IsMerchant;

    /// <summary>True tant qu'au moins un joueur tient le lock d'interaction sur ce NPC.</summary>
    public bool IsInteractionLocked => _interactionOwners.Count > 0;

    /// <summary>
    /// Pose un lock d'interaction pour cet owner (Transform du joueur). Idempotent : si déjà
    /// owner, ne fait rien. Au passage 0 → 1+, l'agent est DÉSACTIVÉ (même raison que knockdown :
    /// éviter qu'un NavMeshObstacle ne pousse le transform) et la state machine cesse d'être
    /// tickée (les timers internes — Merchant._tendTimer, etc. — restent figés). Appelé
    /// exclusivement par NpcInteractionService.
    /// </summary>
    public void ServerBeginInteraction(Transform playerTransform) {
        if (playerTransform == null) return;
        if (_interactionOwners.Contains(playerTransform)) return;
        bool wasLocked = IsInteractionLocked;
        _interactionOwners.Add(playerTransform);
        if (!wasLocked) {
            StopAgent();
            SetAgentEnabled(false);
        }
    }

    /// <summary>
    /// Retire le lock pour cet owner. Au passage 1 → 0, l'agent est RÉACTIVÉ et la state machine
    /// reprend son tick naturel (Merchant repart en Tending pile où il en était). Idempotent.
    /// Appelé exclusivement par NpcInteractionService.
    /// </summary>
    public void ServerEndInteraction(Transform playerTransform) {
        if (playerTransform == null) return;
        if (!_interactionOwners.Remove(playerTransform)) return;
        if (!IsInteractionLocked) {
            SetAgentEnabled(true);
            // Force un broadcast frais au prochain Tick (sortie d'Interacting → vrai état IA).
            _lastNotifiedState = (NpcStateType)255;
        }
    }

    /// <summary>
    /// Vide la liste des owners (release brutal, ex : knockdown ou despawn). Réactive l'agent si
    /// nécessaire. NE notifie PAS les clients ici — c'est le rôle du NpcInteractionService qui
    /// envoie les Responses + nettoie ses dicts.
    /// </summary>
    public void ServerForceReleaseAllInteractions() {
        if (_interactionOwners.Count == 0) return;
        _interactionOwners.Clear();
        SetAgentEnabled(true);
        _lastNotifiedState = (NpcStateType)255;
    }

    public void SetAgentEnabled(bool value) {
        if (_agent != null) _agent.enabled = value;
    }

    /// <summary>Oriente le NPC face à l'orientation autorée de son stand (home).</summary>
    public void FaceHome() {
        if (_home != null) transform.rotation = _home.Rotation;
    }

    /// <summary>Mémorise le dernier point d'intérêt visité pour éviter de le re-piquer.</summary>
    public InterestPoint LastVisitedInterest { get; set; }

    /// <summary>
    /// Appelé par NpcPool.Get() avant SetActive(true).
    /// Injecte les données de spawn (home, identité, room, config résolue).
    /// </summary>
    public void ConfigureForSpawn(NpcSpawnPoint home, NpcIdentity identity,
                                   string newRoomId, NpcConfig config) {
        _home       = home;
        _identity   = identity;
        // Config résolue par le NpcSpawnManager (avec fallback « default »). Re-affectée à CHAQUE
        // spawn : un GO recyclé adopte la config du point courant (marchand ↔ passant).
        _config     = config;
        this.roomId = newRoomId ?? this.roomId;
    }

    /// <summary>
    /// Efface tout état transient de la vie précédente du NPC.
    /// Appelé par NpcPool.Get() après ConfigureForSpawn, avant SetActive(true).
    /// IMPORTANT : ne réinitialise PAS les données injectées par ConfigureForSpawn.
    /// </summary>
    public void ResetForPool() {
        _visitCount          = 0;
        _visitTarget         = 0;
        _npcId               = -1;
        LastVisitedInterest  = null;
        _lastNotifiedState   = (NpcStateType)255;
        _stateMachine        = null;
        _idleState           = null;
        _goToState           = null;
        _backHomeState       = null;
        _sitState            = null;
        _merchantState       = null;
        _merchantPause       = null;
        _interactionOwners.Clear();
        // NB : on n'efface PAS _config ici (re-affecté dans ConfigureForSpawn, appelé AVANT).
        GameLogger.Network.Debug("[NPCPool] ResetForPool complete {ConfigId} {FullName}",
            ConfigId, _identity.FullName);
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

        // Plafonne la vitesse pour rester dans l'animation Walk (cf. blend tree "Velocity").
        if (_agent != null) _agent.speed = walkSpeed;
    }

    /// <summary>
    /// Enregistre le NPC auprès de NpcServerManager et démarre la state machine.
    /// Déclenché par NpcPool.Get() → SetActive(true).
    /// </summary>
    private void OnEnable() {
        if (!NetworkServer.active) {
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
            roomId, transform.position, transform.rotation, styleJson, _identity,
            ConfigId);

        _byId[_npcId] = this;
        _knockdownUntil = -1f;

        Sim.Missions.MissionTargetHooks.RegisterNpc(this, _npcId);

        _visitTarget = Random.Range(minVisitsBeforeReturn, maxVisitsBeforeReturn + 1);
        BuildStateMachine();

        GameLogger.Network.Debug("[NPCPool] Reusing NPC from pool {NpcId} {FullName}",
            _npcId, _identity.FullName);
    }

    /// <summary>
    /// Libère les sièges, désenregistre du NpcServerManager, stoppe la navigation.
    /// Déclenché par NpcPool.Release() → SetActive(false).
    /// </summary>
    private void OnDisable() {
        if (!NetworkServer.active || _npcId <= 0) return;

        _byId.Remove(_npcId);

        // Libère tout siège tenu par ce NPC.
        SeatService.ReleaseAllSeats(this, roomId);

        Sim.Missions.MissionTargetHooks.UnregisterNpc(this);

        // Nettoie silencieusement le service d'interaction : le S2C_DestroyNpc qui suit
        // déclenche déjà la fermeture des sessions côté client (via OnNpcDestroyed event),
        // pas besoin d'envoyer de Response refus.
        NpcInteractionService.Instance?.OnNpcDespawned(_npcId);
        ServerForceReleaseAllInteractions();

        // Désenregistre (broadcast S2C_DestroyNpc aux clients).
        NpcServerManager.Instance.Unregister(_npcId);

        // Stoppe la navigation.
        if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();

        GameLogger.Network.Debug("[NPCPool] Returning NPC to pool {NpcId}", _npcId);
        _npcId = -1;
    }

    /// <summary>
    /// Filet de sécurité au cas où le GO est détruit pendant qu'il est actif
    /// (ex. NpcPool.Dispose() en fin de session serveur).
    /// </summary>
    private void OnDestroy() {
        if (NetworkServer.active && _npcId > 0) {
            _byId.Remove(_npcId);
            SeatService.ReleaseAllSeats(this, roomId);
            NpcServerManager.Instance.Unregister(_npcId);
            NpcSpawnManager.Instance.OnNpcDestroyed(this);
            _npcId = -1;
        }
    }

    /// <summary>Renverse ce NPC (ragdoll) : pause l'IA ~3 s, DÉSACTIVE l'agent, diffuse l'état
    /// KnockedDown aux clients (effondrement sur place, sans projection). Appelé par le serveur
    /// (relais du hit véhicule). Renvoie vrai si le renversement vient de démarrer (faux si déjà au
    /// sol) — l'appelant n'émet le son de choc qu'au renversement initial.
    ///
    /// CRITIQUE : on DÉSACTIVE l'agent (pas juste ResetPath) pour que le NavMeshObstacle du véhicule
    /// (carving=true) ne POUSSE pas le transform du NPC quand le véhicule s'arrête sur le corps. Sans
    /// ça, l'agent NPC se fait dégager hors de la zone carved, déplace transform.position, les
    /// transforms des os du ragdoll héritent du mouvement parent — alors que leurs Rigidbody ne
    /// suivent pas — les joints sont étirés chaque frame → corrections violentes → squelette en folie.
    /// L'agent est réactivé automatiquement à la fin du knockdown (cf. Update).</summary>
    public bool ServerKnockDown() {
        if (!NetworkServer.active || _npcId <= 0) return false;
        if (_knockdownUntil > 0f && Time.time < _knockdownUntil) return false; // déjà au sol
        _knockdownUntil = Time.time + KnockdownDuration;
        // Force la fin de toutes les interactions en cours : les owners ferment leurs modales
        // via Response(KnockedDown). Vidage local des Transforms ensuite.
        NpcInteractionService.Instance?.ReleaseAllForNpc(_npcId, NpcInteractionReason.KnockedDown);
        ServerForceReleaseAllInteractions();
        StopAgent();
        SetAgentEnabled(false);
        NpcServerManager.Instance.Knockdown(_npcId);
        return true;
    }

    // Mémo du state envoyé pour détecter les transitions et déclencher
    // un broadcast immédiat (cf. NpcServerManager.NotifyStateChanged).
    private NpcStateType _lastNotifiedState = (NpcStateType)255;

    private void Update() {
        if (!NetworkServer.active || _stateMachine == null) return;

        // Suspend all AI work if the room is inactive (no players present).
        if (!RoomActivityController.Instance.IsRoomActive(roomId)) return;

        // Renversé : IA en pause, on diffuse l'état KnockedDown (position figée) jusqu'à la relève.
        if (_knockdownUntil > 0f && Time.time < _knockdownUntil) {
            NpcServerManager.Instance.PushTransform(
                _npcId, transform.position, transform.rotation, Vector3.zero, NpcStateType.KnockedDown);
            if (_lastNotifiedState != NpcStateType.KnockedDown) {
                NpcServerManager.Instance.NotifyStateChanged(_npcId, NpcStateType.KnockedDown);
                _lastNotifiedState = NpcStateType.KnockedDown;
            }
            return;
        }

        // Sortie de knockdown : si le timer a expiré ET qu'il avait été armé, on réactive l'agent
        // désactivé par ServerKnockDown. Le test `_knockdownUntil > 0` agit comme un edge-trigger ;
        // on remet à -1 pour ne pas ré-enabler à chaque frame.
        if (_knockdownUntil > 0f) {
            _knockdownUntil = -1f;
            SetAgentEnabled(true);
        }

        // Freeze d'interaction : tant qu'un joueur tient le lock, on skip _stateMachine.Tick()
        // (timers internes figés : NpcMerchantState._tendTimer reste où il en était), on rotate
        // vers l'owner le plus proche, et on diffuse l'état Interacting.
        if (IsInteractionLocked) {
            TickInteractionOverlay();
            return;
        }

        _stateMachine.Tick();

        // L'état logique vient directement de la state machine (source unique).
        NpcStateType current = (_stateMachine.CurrentState is NpcStateBase nsb)
            ? nsb.StateType
            : NpcStateType.Idle;

        // Walking est dérivé du couple (Idle, vélocité) pour préserver l'animation
        // "Walk" sur les blend trees existants pendant les déplacements rapides.
        if (current == NpcStateType.Idle && _agent.velocity.sqrMagnitude > 0.01f) {
            current = NpcStateType.Walking;
        }

        // ORDRE CRITIQUE : PushTransform d'abord (position fraîche), NotifyStateChanged ensuite.
        Vector3 vel = (_agent != null && _agent.enabled) ? _agent.velocity : Vector3.zero;

        NpcServerManager.Instance.PushTransform(
            _npcId,
            transform.position,
            transform.rotation,
            vel,
            current
        );

        if (current != _lastNotifiedState) {
            NpcServerManager.Instance.NotifyStateChanged(_npcId, current);
            _lastNotifiedState = current;
        }
    }

    /// <summary>
    /// Tick du freeze d'interaction. Filtre les Transforms devenus null (despawn joueur),
    /// rotate vers l'owner le plus proche horizontalement, diffuse Interacting au snapshot.
    /// Si la liste tombe à 0 après filtrage, on libère le freeze et la state machine reprend
    /// au tick suivant.
    /// </summary>
    private void TickInteractionOverlay() {
        // Purge des Transforms despawned (Unity ==null check).
        for (int i = _interactionOwners.Count - 1; i >= 0; i--) {
            if (_interactionOwners[i] == null) _interactionOwners.RemoveAt(i);
        }
        if (_interactionOwners.Count == 0) {
            SetAgentEnabled(true);
            _lastNotifiedState = (NpcStateType)255;
            return;
        }

        // Cible la plus proche, distance horizontale (XZ).
        Vector3 self = transform.position;
        Transform closest = _interactionOwners[0];
        float bestSqr = float.PositiveInfinity;
        for (int i = 0; i < _interactionOwners.Count; i++) {
            Vector3 d = _interactionOwners[i].position - self;
            d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; closest = _interactionOwners[i]; }
        }

        // Rotation vers le joueur (skippée si quasi-superposé pour éviter la singularité).
        if (bestSqr >= InteractionFaceMinDistanceSqr) {
            Vector3 dir = closest.position - self;
            dir.y = 0f;
            Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, InteractionRotationSpeedDeg * Time.deltaTime);
        }

        // Snapshot Interacting : vélocité forcée à 0 (l'agent est désactivé).
        NpcServerManager.Instance.PushTransform(
            _npcId, transform.position, transform.rotation, Vector3.zero, NpcStateType.Interacting);
        if (_lastNotifiedState != NpcStateType.Interacting) {
            NpcServerManager.Instance.NotifyStateChanged(_npcId, NpcStateType.Interacting);
            _lastNotifiedState = NpcStateType.Interacting;
        }
    }

    // ── State machine wiring ──────────────────────────────────────────────────

    private void BuildStateMachine() {
        if (IsMerchant) {
            BuildMerchantStateMachine();
            return;
        }

        _stateMachine  = new StateMachine();
        _idleState     = new NpcIdleState(this);
        _goToState     = new NpcGoToInterestAreaState(this);
        _backHomeState = new NpcBackToHomeState(this);
        _sitState      = new NpcSitState(this);

        _stateMachine.AddTransition(_idleState, _backHomeState,
            () => _idleState.IsIdleComplete && ShouldReturnHome());
        _stateMachine.AddTransition(_idleState, _sitState,
            () => _idleState.IsIdleComplete && !ShouldReturnHome() && DecideSit());
        _stateMachine.AddTransition(_idleState, _goToState,
            () => _idleState.IsIdleComplete && !ShouldReturnHome());

        _stateMachine.AddTransition(_goToState, _idleState, () => _goToState.HasArrived);
        _stateMachine.AddTransition(_sitState,  _idleState, () => _sitState.HasFinished);

        _stateMachine.SetState(_idleState);
    }

    /// <summary>
    /// Sous-graphe marchand : tient le stand ↔ courte pause. Aucune arête vers BackToHome →
    /// le marchand ne despawn jamais par compteur de visites ; il tient le stand tant que la
    /// room est active.
    /// </summary>
    private void BuildMerchantStateMachine() {
        _stateMachine  = new StateMachine();
        _merchantState = new NpcMerchantState(this);
        _merchantPause = new NpcMerchantPauseState(this);

        _stateMachine.AddTransition(_merchantState, _merchantPause,
            () => _merchantState.WantsPause);
        _stateMachine.AddTransition(_merchantPause, _merchantState,
            () => _merchantPause.HasFinished);

        _stateMachine.SetState(_merchantState);
    }

    private bool DecideSit() => Random.value < sitProbability;
}
