using System;
using Interaction;
using Sim;
using Sim.Enums;
using Sim.Logging;
using UnityEngine;
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

    // Action LOOK instanciée depuis Resources à l'Awake — même pattern que PlayerController.SetupActions.
    private Action _lookAction;

    public int      NpcId     { get; private set; }
    public string   RoomId    { get; private set; }
    public string   FirstName { get; private set; }
    public string   LastName  { get; private set; }
    public MoodEnum Mood      { get; private set; }
    public string   FullName  => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";

    // ── IInteractable ─────────────────────────────────────────────────────────

    /// <summary>Portée d'interaction NPC (mètres).</summary>
    public float GetRange() => 3f;

    public bool IsInteractable() => _lookAction != null;

    public bool IsRightClickOnly() => false;

    public void StopInteraction() { }

    public Action[] GetActions(bool withPriority = false) {
        if (_lookAction == null) return Array.Empty<Action>();
        return new[] { _lookAction };
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake() {
        _animator   = GetComponent<PlayerAnimator>();
        _styleSetup = GetComponent<CharacterStyleSetup>();
        _ragdoll    = GetComponent<RagdollController>();

        // Charge et instancie une copie de l'asset LOOK (même pattern que PlayerController).
        // Chaque vue dispose de sa propre instance pour éviter le partage d'event delegates.
        Action template = Resources.Load<Action>("Configurations/Actions/LOOK");
        if (template != null) {
            _lookAction = UnityEngine.Object.Instantiate(template);
            _lookAction.OnExecute += OnLookExecuted;
        }
        else {
            Debug.LogWarning("[ClientNpcView] LOOK action asset not found at Resources/Configurations/Actions/LOOK");
        }
    }

    private void OnDestroy() {
        if (_lookAction != null) {
            _lookAction.OnExecute -= OnLookExecuted;
            UnityEngine.Object.Destroy(_lookAction);
            _lookAction = null;
        }
    }

    // ── Initialisation / snapshots ────────────────────────────────────────────

    /// <summary>Initialise le NPC à la réception d'un S2C_SpawnNpc.</summary>
    public void Init(int npcId, string roomId, string styleJson,
                     string firstName, string lastName, byte mood) {
        NpcId     = npcId;
        RoomId    = roomId;
        FirstName = firstName ?? string.Empty;
        LastName  = lastName  ?? string.Empty;
        Mood      = (MoodEnum)mood;

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

    private void EnterKnockdownVisual() {
        _knockedDown = true;
        if (_ragdoll != null) _ragdoll.EnableRagdoll();
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
    }

    public void PushSnapshot(Vector3 position, Quaternion rotation,
                             Vector3 velocity, NpcStateType state) {
        if (state != _currentState) {
            ClientLogger.Network("NpcStateReceived {NpcId} {From} {To}", NpcId, _currentState, state);
            bool wasKnocked = _currentState == NpcStateType.KnockedDown;
            _currentState = state;
            if (state == NpcStateType.KnockedDown) EnterKnockdownVisual();
            else if (wasKnocked)                   ExitKnockdownVisual(position, rotation);
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

        _lastVelocity   = isSitting ? Vector3.zero : velocity;
        _displayedSpeed = isSitting ? 0f : velocity.magnitude;
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

    // ── Interaction handlers ──────────────────────────────────────────────────

    private void OnLookExecuted(Action action) {
        ClientLogger.Network("[NPCInteraction] LOOK action requested {NpcId} {FullName}", NpcId, FullName);
        PlayerController.Local?.Look(transform);
        ClientLogger.Network("[NPCInteraction] LOOK applied {NpcId}", NpcId);
    }
}
