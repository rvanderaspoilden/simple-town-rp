using System;
using Interaction;
using Sim;
using Sim.Enums;
using Sim.Logging;
using Sim.NPC;
using Sim.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Action = Sim.Interactables.Action;

/// <summary>
/// Vue locale d'un NPC côté client. Reçoit des snapshots discrets et interpole
/// linéairement avec un léger délai pour absorber le jitter.
///
/// Implémente IInteractable pour s'intégrer au pipeline d'interaction existant
/// (CameraManager raycast → ShowContextMenu → DoAction). Action disponible : LOOK.
///
/// Pas de gameplay/réseau ici :
///   - Aucun NavMeshAgent côté client.
///   - Aucun rigidbody / physique.
///   - Aucune logique IA.
/// </summary>
public class ClientNpcView : MonoBehaviour, IInteractable {
    [Header("Interpolation")]
    [Tooltip("Délai (s) appliqué au rendu pour absorber le jitter réseau. Doit couvrir 4-6 " +
             "snapshots à la cadence d'envoi (sendRate=30 Hz → 33 ms par snapshot → 0.18 à 0.25). " +
             "Trop bas = micro-pauses dès qu'un snapshot est en retard ; trop haut = NPC visiblement " +
             "à la traîne.")]
    [SerializeField] private float interpolationDelay = 0.20f;

    [Tooltip("Durée max (s) pendant laquelle on EXTRAPOLE la position avec la dernière velocity " +
             "reçue quand aucun nouveau snapshot n'est arrivé. Évite le gel net lors d'un trou " +
             "réseau ; 0 = pas d'extrapolation (gel immédiat).")]
    [SerializeField] private float extrapolationLimit = 0.15f;

    [Tooltip("Distance au-delà de laquelle on snap (téléport) plutôt qu'interpole.")]
    [SerializeField] private float snapDistance = 8f;

    [Header("Affichage du nom (optionnel)")]
    [Tooltip("Si assigné, le label sera mis à jour avec le nom complet du NPC.")]
    [SerializeField] private Text nameLabel;

    [Header("Icône de type (optionnel)")]
    [Tooltip("Canvas monde autoré dans le prefab (inactif par défaut). Activé à l'Init si la " +
             "NpcConfig porte une icône ; billboard face caméra en LateUpdate.")]
    [SerializeField] private GameObject iconRoot;
    [Tooltip("Image du canvas icône, alimentée par NpcConfig.Icon.")]
    [SerializeField] private Image iconImage;

    private struct Snapshot {
        public float      ServerTime;
        public Vector3    Position;
        public Quaternion Rotation;
    }

    private Snapshot _prev;
    private Snapshot _next;
    private bool     _hasPrev;
    private bool     _hasNext;
    private float    _displayedSpeed;
    // Velocity DIRECTIONNELLE du dernier snapshot, utilisée pour extrapoler quand
    // _next est dépassé sans nouveau snapshot reçu (trou réseau).
    private Vector3  _lastVelocity;

    private NpcStateType _currentState = NpcStateType.Idle;
    private NpcStateType _appliedState = (NpcStateType)255;

    private PlayerAnimator      _animator;
    private CharacterStyleSetup _styleSetup;
    private RagdollController    _ragdoll;
    private bool                 _knockedDown;

    // Actions TALK/SEARCH instanciées depuis Resources à l'Awake — même pattern que
    // PlayerController.SetupActions. Chaque vue a sa propre copie (pas de partage d'event delegates).
    private Action _talkAction;
    private Action _searchAction;

    // Config du NPC, rechargée depuis Resources via DatabaseManager.GetNpcConfigById(ConfigId) à l'Init.
    // Fallback sur DatabaseManager.DefaultNpcConfig pour les passants sans config. Porte la nature
    // marchande (label + merchant) et le dialogue.
    private string    _configId;
    private NpcConfig _config;

    public int      NpcId     { get; private set; }
    public string   RoomId    { get; private set; }
    public string   FirstName { get; private set; }
    public string   LastName  { get; private set; }
    public MoodEnum Mood      { get; private set; }
    public string   FullName  => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";

    /// <summary>Config résolue du NPC (jamais null après Init si un NpcConfig « default » existe).</summary>
    public NpcConfig Config        => _config;
    public bool      IsMerchant    => _config != null && _config.IsMerchant;
    /// <summary>Libellé marchand (null si non-marchand). Dérivé de la config rechargée par id.</summary>
    public string    MerchantLabel => (_config as MerchantNpcConfig)?.MerchantLabel;

    /// <summary>Le NPC est-il MORT (état Dead du serveur) ? Le cadavre ragdoll devient
    /// « fouillable » : l'interaction propose SEARCH au lieu de TALK.</summary>
    public bool IsDead => _currentState == NpcStateType.Dead;

    // ── IInteractable ─────────────────────────────────────────────────────────

    /// <summary>Portée d'interaction NPC (mètres) — règle projet : 2 m pour PNJ et joueurs.</summary>
    public float GetRange() => 2f;

    public bool IsInteractable() => IsDead ? _searchAction != null : _talkAction != null;

    public bool IsRightClickOnly() => false;

    public void StopInteraction() { }

    public Action[] GetActions(bool withPriority = false) {
        // Vivant : TALK est la seule action (le shop marchand s'ouvre via une réponse de
        // dialogue). Mort : SEARCH (« Fouiller ») est la seule action du cadavre.
        if (IsDead) return _searchAction != null ? new[] { _searchAction } : Array.Empty<Action>();
        if (_talkAction == null) return Array.Empty<Action>();
        return new[] { _talkAction };
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake() {
        _animator   = GetComponent<PlayerAnimator>();
        _styleSetup = GetComponent<CharacterStyleSetup>();
        _ragdoll    = GetComponent<RagdollController>();

        // Le NavMeshAgent vit côté serveur (NpcAIController). Sur le client il est un vestige
        // du prefab et DOIT être désactivé : sinon, quand la NavMeshObstacle du véhicule
        // (carving=true) creuse la NavMesh sous le NPC à l'arrêt, l'agent éjecte le transform
        // hors de la zone carved → les os du ragdoll, enfants du transform, suivent en physique
        // dynamique → les joints s'étirent → squelette qui s'agite dans tous les directions.
        // Pattern identique au PlayerController.OnStartClient (agent désactivé sur les remotes).
        if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;

        // Charge et instancie une copie de l'asset TALK (même pattern que PlayerController).
        // Chaque vue dispose de sa propre instance pour éviter le partage d'event delegates.
        Action talkTemplate = Resources.Load<Action>("Configurations/Actions/TALK");
        if (talkTemplate != null) {
            _talkAction = UnityEngine.Object.Instantiate(talkTemplate);
            _talkAction.OnExecute += OnTalkExecuted;
        }
        else {
            Debug.LogWarning("[ClientNpcView] TALK action asset not found at Resources/Configurations/Actions/TALK");
        }

        Action searchTemplate = Resources.Load<Action>("Configurations/Actions/SEARCH");
        if (searchTemplate != null) {
            _searchAction = UnityEngine.Object.Instantiate(searchTemplate);
            _searchAction.OnExecute += OnSearchExecuted;
        }
        else {
            Debug.LogWarning("[ClientNpcView] SEARCH action asset not found at Resources/Configurations/Actions/SEARCH");
        }
    }

    private void OnDestroy() {
        // Si une session d'interaction pointait sur ce NPC (despawn pendant un dialogue,
        // changement de room, fermeture serveur), on ferme silencieusement : le serveur a
        // déjà cleané via OnNpcDespawned, inutile de renvoyer un C2S End.
        if (NpcInteractionSession.ActiveNpcId == NpcId) {
            NpcInteractionSession.EndSilent(NpcId);
        }

        if (_talkAction != null) {
            _talkAction.OnExecute -= OnTalkExecuted;
            UnityEngine.Object.Destroy(_talkAction);
            _talkAction = null;
        }

        if (_searchAction != null) {
            _searchAction.OnExecute -= OnSearchExecuted;
            UnityEngine.Object.Destroy(_searchAction);
            _searchAction = null;
        }
    }

    // ── Initialisation / snapshots ────────────────────────────────────────────

    /// <summary>Initialise le NPC à la réception d'un S2C_SpawnNpc.</summary>
    public void Init(int npcId, string roomId, string styleJson,
                     string firstName, string lastName, byte mood, string configId = null) {
        NpcId         = npcId;
        RoomId        = roomId;
        FirstName     = firstName ?? string.Empty;
        LastName      = lastName  ?? string.Empty;
        Mood          = (MoodEnum)mood;

        // Recharge la config par id (réplication par id). Fallback sur la config « default »
        // pour les passants sans config → tout le monde a au moins un dialogue par défaut.
        _configId = configId ?? string.Empty;
        _config   = DatabaseManager.GetNpcConfigById(_configId);
        if (_config == null) _config = DatabaseManager.DefaultNpcConfig;

        if (_styleSetup != null && !string.IsNullOrEmpty(styleJson)) {
            try {
                Style style = JsonUtility.FromJson<Style>(styleJson);
                _styleSetup.ApplyStyle(style);
            }
            catch (Exception e) {
                Debug.LogWarning($"[ClientNpcView] Failed to apply style for npc#{npcId}: {e.Message}");
            }
        }

        if (_animator != null) {
            _animator.SetMood((int)Mood);
        }

        if (nameLabel != null) {
            nameLabel.text = FullName;
        }

        RefreshIcon();

        _prev = _next = new Snapshot {
            ServerTime = Time.time,
            Position   = transform.position,
            Rotation   = transform.rotation
        };
        _hasPrev = _hasNext = true;
    }

    /// <summary>Reçu via S2C_NpcKnockdown (one-shot) : déclenche l'effondrement ragdoll sur place.</summary>
    public void ApplyKnockdown() {
        EnterKnockdownVisual();
    }

    /// <summary>Reçu via S2C_NpcHitReact (one-shot) : coup NON létal encaissé — petit choc
    /// procédural du buste (CharacterFlinch), le NPC reste debout.</summary>
    public void PlayHitReact() {
        if (_knockedDown) return; // au sol : la physique ragdoll suffit
        CharacterFlinch.PlayOn(gameObject);
    }

    private void EnterKnockdownVisual() {
        _knockedDown = true;
        if (_ragdoll != null) _ragdoll.EnableRagdoll();
        RefreshIcon(); // le corps s'effondre ailleurs que le transform racine → icône masquée
    }

    private void ExitKnockdownVisual(Vector3 position, Quaternion rotation) {
        _knockedDown = false;
        if (_ragdoll != null) _ragdoll.DisableRagdoll();
        // Le NPC se relève là où le serveur le situe (position du hit).
        transform.position = position;
        transform.rotation = rotation;
        _prev = _next = new Snapshot { ServerTime = Time.time, Position = position, Rotation = rotation };
        _hasPrev = _hasNext = true;
        _appliedState = (NpcStateType)255; // force la ré-application de l'état animator
        RefreshIcon();
    }

    /// <summary>
    /// Icône de type (marchand, concessionnaire…) : visible seulement si la config en porte une
    /// et que le NPC est debout. Le canvas est AUTORÉ dans le prefab client (inactif par défaut).
    /// </summary>
    private void RefreshIcon() {
        if (iconRoot == null) return;
        Sprite icon = _config != null ? _config.Icon : null;
        if (iconImage != null) iconImage.sprite = icon;
        iconRoot.SetActive(icon != null && iconImage != null && !_knockedDown);
    }

    public void PushSnapshot(Vector3 position, Quaternion rotation,
                             Vector3 velocity, NpcStateType state) {
        if (state != _currentState) {
            ClientLogger.Network("NpcStateReceived {NpcId} {From} {To}", NpcId, _currentState, state);
            // KnockedDown (renversement véhicule, relève) et Dead (cadavre jusqu'au despawn)
            // partagent le même visuel ragdoll « au sol ».
            bool wasDown = _currentState == NpcStateType.KnockedDown || _currentState == NpcStateType.Dead;
            bool isDown  = state == NpcStateType.KnockedDown || state == NpcStateType.Dead;
            _currentState = state;
            if (isDown && !wasDown)      EnterKnockdownVisual();
            else if (wasDown && !isDown) ExitKnockdownVisual(position, rotation);
        }

        if (_knockedDown) {
            // Position figée : la physique ragdoll locale pilote la pose, on n'alimente pas l'interpolation.
            _lastVelocity   = Vector3.zero;
            _displayedSpeed = 0f;
            return;
        }

        bool isSitting = state == NpcStateType.Sitting;
        bool snapJump  = _hasNext && (position - _next.Position).sqrMagnitude > snapDistance * snapDistance;

        if (isSitting || snapJump) {
            transform.position = position;
            transform.rotation = rotation;
            _prev = _next = new Snapshot {
                ServerTime = Time.time,
                Position   = position,
                Rotation   = rotation
            };
        }
        else {
            _prev = _hasNext ? _next : new Snapshot {
                ServerTime = Time.time,
                Position   = transform.position,
                Rotation   = transform.rotation
            };
            _next = new Snapshot {
                ServerTime = Time.time,
                Position   = position,
                Rotation   = rotation
            };
        }
        _hasPrev = _hasNext = true;

        // Interacting (freeze overlay serveur) = NPC immobile debout : vélocité forcée à 0 pour
        // garantir l'animation Idle (la vélocité reçue est déjà 0 côté serveur, mais on est ceinture-bretelles).
        bool forceStill = isSitting || state == NpcStateType.Interacting;
        _lastVelocity   = forceStill ? Vector3.zero : velocity;
        _displayedSpeed = forceStill ? 0f : velocity.magnitude;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update() {
        if (!_hasPrev || !_hasNext) return;
        if (_knockedDown) return; // ragdoll local : pas d'interpolation ni d'animator

        if (_currentState == NpcStateType.Sitting) {
            transform.position = _next.Position;
            transform.rotation = _next.Rotation;
        }
        else {
            float renderTime = Time.time - interpolationDelay;
            float duration   = Mathf.Max(0.0001f, _next.ServerTime - _prev.ServerTime);
            float rawT       = (renderTime - _prev.ServerTime) / duration;

            if (rawT <= 1f) {
                // Interpolation normale entre _prev et _next.
                float t = Mathf.Clamp01(rawT);
                transform.position = Vector3.Lerp(_prev.Position, _next.Position, t);
                transform.rotation = Quaternion.Slerp(_prev.Rotation, _next.Rotation, t);
            }
            else {
                // _next dépassé sans nouveau snapshot : on EXTRAPOLE avec la dernière
                // velocity reçue, plafonné par extrapolationLimit pour ne pas dériver.
                // Évite le gel sec (et donc l'effet "avance / pause / avance") sur jitter.
                float overshoot = Mathf.Min((renderTime - _next.ServerTime), extrapolationLimit);
                transform.position = _next.Position + _lastVelocity * overshoot;
                transform.rotation = _next.Rotation;
            }
        }

        if (_animator != null) {
            _animator.SetVelocity(_displayedSpeed);

            if (_appliedState != _currentState) {
                if (_currentState == NpcStateType.Sitting) {
                    _animator.SetAction(CharacterAnimatorAction.SIT);
                    ClientLogger.Network("NpcAppliedSit {NpcId}", NpcId);
                }
                else if (_appliedState == NpcStateType.Sitting) {
                    _animator.SetAction(CharacterAnimatorAction.NONE);
                    ClientLogger.Network("NpcAppliedUnsit {NpcId}", NpcId);
                }
                _appliedState = _currentState;
            }
        }
    }

    private void LateUpdate() {
        // Billboard de l'icône face caméra (même pattern que les world toasts / billboards de prix).
        if (iconRoot == null || !iconRoot.activeSelf) return;
        Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
        if (cam == null) return;
        iconRoot.transform.rotation =
            Quaternion.LookRotation(iconRoot.transform.position - cam.transform.position, Vector3.up);
    }

    // ── Interaction handlers ──────────────────────────────────────────────────

    private void OnTalkExecuted(Action action) {
        ClientLogger.Network("[NPCInteraction] TALK action requested {NpcId} {FullName}", NpcId, FullName);

        // Dialogue de cette config, sinon fallback sur le dialogue de la config « default ».
        DialogueConfig dialogue = _config != null ? _config.Dialogue : null;
        if (dialogue == null && DatabaseManager.DefaultNpcConfig != null)
            dialogue = DatabaseManager.DefaultNpcConfig.Dialogue;

        if (dialogue == null) {
            ClientLogger.Network("[NPCInteraction] TALK no dialogue {NpcId}", NpcId);
            return;
        }

        // Tourne le joueur vers le NPC, puis ouvre la modale de dialogue.
        PlayerController.Local?.Look(transform);
        DialogueUI.Instance?.Show(NpcId, FullName, dialogue);
    }

    /// <summary>Action SEARCH (« Fouiller ») sur le cadavre : ouvre optimistement la grille
    /// de butin (panneau conteneur, canal Prop — même flux que le coffre de véhicule) et
    /// demande la fouille au serveur, qui valide (mort, portée), génère le loot de la
    /// NpcConfig À LA PREMIÈRE fouille puis répond S2C_ContainerOpened avec le restant.</summary>
    private void OnSearchExecuted(Action action) {
        if (!IsDead || !Mirror.NetworkClient.isConnected) return;
        ClientLogger.Network("[NPCInteraction] SEARCH corpse requested {NpcId} {FullName}", NpcId, FullName);
        // Occupe le joueur (BUSY) AVANT d'ouvrir la grille : la sortie d'Idle referme
        // l'inventaire, donc l'ordre compte. L'état se termine seul à la fermeture de la grille.
        PlayerController.Local?.BeginBusy(ContainerPanelUI.IsPropLootOpen, transform);
        ContainerPanelUI.Prop?.OpenOptimistic(ServerItemManager.CorpseLootSlots, $"Fouille — {FullName}");
        Mirror.NetworkClient.Send(new C2S_SearchNpcCorpse { NpcId = NpcId });
    }
}
