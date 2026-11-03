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
public class ClientNpcView : MonoBehaviour, IInteractable, IQuickActionProvider {
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
    private Animator            _unityAnimator;
    private CharacterStyleSetup _styleSetup;
    private RagdollController    _ragdoll;
    private bool                 _knockedDown;

    // NPC hostile : anim d'attaque procédurale + arme visuelle en main droite (prefab de
    // l'item de la HostileNpcConfig, purement cosmétique — la logique vit côté serveur).
    private NpcAttackAnimator _attackAnimator;
    private ItemBehaviour     _weaponVisual;

    private static readonly Color HostileNameColor   = new Color(1f, 0.42f, 0.36f);
    private static readonly Color HostileIconIdle    = new Color(1f, 0.55f, 0.25f);
    private static readonly Color HostileIconAggro   = new Color(1f, 0.15f, 0.1f);

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
    public bool      IsHostile     => _config != null && _config.IsHostile;
    /// <summary>Libellé marchand (null si non-marchand). Dérivé de la config rechargée par id.</summary>
    public string    MerchantLabel => (_config as MerchantNpcConfig)?.MerchantLabel;

    /// <summary>Le NPC est-il MORT (état Dead du serveur) ? Le cadavre ragdoll devient
    /// « fouillable » : l'interaction propose SEARCH au lieu de TALK.</summary>
    public bool IsDead => _currentState == NpcStateType.Dead;

    // ── IInteractable ─────────────────────────────────────────────────────────

    /// <summary>Portée d'interaction NPC (mètres) — règle projet : 2 m pour PNJ et joueurs.</summary>
    public float GetRange() => 2f;

    public bool IsInteractable() {
        if (IsDead) return _searchAction != null;
        // Hostile vivant : aucune interaction (il attaque à vue) — le serveur refuse aussi.
        if (IsHostile) return false;
        return _talkAction != null;
    }

    public bool IsRightClickOnly() => false;

    public void StopInteraction() { }

    /// <summary>E court sur un CADAVRE = fouille directe (SEARCH), sans passer par le menu
    /// radial. Vivant : pas d'action rapide (TALK garde son flux normal).</summary>
    public bool TryGetQuickAction(Action[] available, out Action action) {
        action = IsDead ? _searchAction : null;
        return action != null;
    }

    public Action[] GetActions(bool withPriority = false) {
        // Vivant : TALK est la seule action (le shop marchand s'ouvre via une réponse de
        // dialogue). Mort : SEARCH (« Fouiller ») est la seule action du cadavre.
        if (IsDead) return _searchAction != null ? new[] { _searchAction } : Array.Empty<Action>();
        if (IsHostile) return Array.Empty<Action>(); // hostile vivant : pas de TALK
        if (_talkAction == null) return Array.Empty<Action>();
        return new[] { _talkAction };
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake() {
        _animator      = GetComponent<PlayerAnimator>();
        _unityAnimator = GetComponent<Animator>();
        _styleSetup    = GetComponent<CharacterStyleSetup>();
        _ragdoll       = GetComponent<RagdollController>();

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
        SetupHostileVisuals();

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
        UpdateHostileIndicator();
    }

    // ── NPC hostile : indicateur, arme visuelle, animations d'attaque ──────────

    /// <summary>Signale l'hostilité du NPC : nom teinté rouge, icône de config (autorée dans
    /// le prefab, cf. iconRoot) teintée orange — rouge vif pendant la poursuite/l'attaque —
    /// et composant d'anim d'attaque procédurale. Appelé à l'Init.</summary>
    private void SetupHostileVisuals() {
        if (!(_config is HostileNpcConfig hostile)) return;

        if (nameLabel != null) nameLabel.color = HostileNameColor;

        _attackAnimator = GetComponent<NpcAttackAnimator>();
        if (_attackAnimator == null) _attackAnimator = gameObject.AddComponent<NpcAttackAnimator>();

        AttachWeaponVisual(hostile.Weapon);
        UpdateHostileIndicator();
    }

    /// <summary>Teinte l'icône au-dessus de la tête selon l'état d'aggro, et tient la pose
    /// de visée (arme à feu) pendant l'état Attacking. Appelé aux changements d'état.</summary>
    private void UpdateHostileIndicator() {
        if (!IsHostile) return;

        bool aggro = _currentState == NpcStateType.Chasing || _currentState == NpcStateType.Attacking;
        if (iconImage != null) iconImage.color = aggro ? HostileIconAggro : HostileIconIdle;

        if (_attackAnimator != null) {
            bool aimHold = _currentState == NpcStateType.Attacking
                           && _config is HostileNpcConfig hostile && hostile.IsRanged
                           && !_knockedDown;
            _attackAnimator.SetAimHold(aimHold);
        }
    }

    /// <summary>Instancie le prefab de l'arme de la config en MAIN DROITE (ancre « Handle
    /// Position » sous l'os de main, comme les items des joueurs), avec le grip de l'ItemConfig.
    /// Purement visuel : colliders coupés, item marqué tenu (jamais survolable).</summary>
    private void AttachWeaponVisual(ItemConfig weaponConfig) {
        if (weaponConfig == null || weaponConfig.Prefab == null || _unityAnimator == null) return;

        Transform hand = _unityAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        Transform anchor = FindDeepChild(hand, "Handle Position");
        if (anchor == null) anchor = hand;

        ItemBehaviour item = Instantiate(weaponConfig.Prefab, anchor);
        item.name = $"NPC Weapon ({weaponConfig.name})";
        item.Initialize(weaponConfig);
        item.transform.localPosition = weaponConfig.HasGripOverride ? weaponConfig.GripPosition : Vector3.zero;
        item.transform.localRotation = weaponConfig.HasGripOverride
            ? Quaternion.Euler(weaponConfig.GripEuler) : Quaternion.identity;
        item.OnAttachedToHand(0, HandType.Right);

        foreach (Collider c in item.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        _weaponVisual = item;
    }

    private static Transform FindDeepChild(Transform parent, string childName) {
        for (int i = 0; i < parent.childCount; i++) {
            Transform child = parent.GetChild(i);
            if (child.name == childName) return child;
            Transform found = FindDeepChild(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Reçu via S2C_NpcAttack (one-shot) : joue l'anim d'attaque procédurale et les
    /// effets de l'arme tenue (whoosh, tracer/flash/son, son d'impact) sur cette vue.</summary>
    public void PlayAttack(NpcAttackKind kind, Vector3 point) {
        if (_knockedDown) return;

        switch (kind) {
            case NpcAttackKind.MeleeSwing:
                _attackAnimator?.PlaySwing();
                (_weaponVisual as MeleeWeaponBehaviour)?.PlaySwingEffects();
                break;

            case NpcAttackKind.PunchRight:
                _attackAnimator?.PlayPunch(false);
                break;

            case NpcAttackKind.PunchLeft:
                _attackAnimator?.PlayPunch(true);
                break;

            case NpcAttackKind.RangedFire:
                _attackAnimator?.PlayFire();
                (_weaponVisual as WeaponBehaviour)?.PlayFireEffects(point);
                break;

            case NpcAttackKind.MeleeImpact:
                (_weaponVisual as MeleeWeaponBehaviour)?.PlayHitEffects(point);
                break;
        }
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
            UpdateHostileIndicator();
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
        // Fouille DIFFÉRÉE : le C2S part tout de suite (l'aller-retour backend tourne pendant
        // le geste de fouille), la grille ne s'affiche qu'à l'échéance — items déjà dedans.
        // BUSY annulable par le MOUVEMENT : bouger pendant la fouille (geste ou grille
        // ouverte) referme la modale et rend la main.
        PlayerController.Local?.BeginBusy(ContainerPanelUI.IsPropLootOpen, transform,
            cancelOnMove: true, onCancelled: () => ContainerPanelUI.Prop?.Close());
        // Durée du geste configurée par la NpcConfig du cadavre (SearchDuration).
        ContainerPanelUI.Prop?.OpenSearchDelayed(ServerItemManager.CorpseLootSlots,
            $"Fouille — {FullName}", _config != null ? _config.SearchDuration : 0.5f);
        Mirror.NetworkClient.Send(new C2S_SearchNpcCorpse { NpcId = NpcId });
    }
}
