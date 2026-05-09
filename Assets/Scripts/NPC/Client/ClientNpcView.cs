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
    [Tooltip("Délai (s) appliqué au rendu pour absorber le jitter. ~ 1/UpdatesPerSecond.")]
    [SerializeField] private float interpolationDelay = 0.12f;

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

    private NpcStateType _currentState = NpcStateType.Idle;
    private NpcStateType _appliedState = (NpcStateType)255;

    private PlayerAnimator      _animator;
    private CharacterStyleSetup _styleSetup;

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

    public void StopInteraction() { }

    public Action[] GetActions(bool withPriority = false) {
        if (_lookAction == null) return Array.Empty<Action>();
        return new[] { _lookAction };
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake() {
        _animator   = GetComponent<PlayerAnimator>();
        _styleSetup = GetComponent<CharacterStyleSetup>();

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

    public void PushSnapshot(Vector3 position, Quaternion rotation,
                             Vector3 velocity, NpcStateType state) {
        if (state != _currentState) {
            ClientLogger.Network("NpcStateReceived {NpcId} {From} {To}", NpcId, _currentState, state);
            _currentState = state;
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

        _displayedSpeed = isSitting ? 0f : velocity.magnitude;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update() {
        if (!_hasPrev || !_hasNext) return;

        if (_currentState == NpcStateType.Sitting) {
            transform.position = _next.Position;
            transform.rotation = _next.Rotation;
        }
        else {
            float renderTime = Time.time - interpolationDelay;
            float duration   = Mathf.Max(0.0001f, _next.ServerTime - _prev.ServerTime);
            float t          = Mathf.Clamp01((renderTime - _prev.ServerTime) / duration);
            transform.position = Vector3.Lerp(_prev.Position, _next.Position, t);
            transform.rotation = Quaternion.Slerp(_prev.Rotation, _next.Rotation, t);
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
        string moodDesc = GetMoodDescription(Mood);
        NotificationManager.Instance?.AddNotification($"{FullName} {moodDesc}", NotificationType.BANK);
        ClientLogger.Network("[NPCInteraction] LOOK result displayed {NpcId}", NpcId);
    }

    private static string GetMoodDescription(MoodEnum mood) {
        return mood switch {
            MoodEnum.HAPPY   => "looks happy.",
            MoodEnum.SAD     => "looks sad.",
            MoodEnum.ANGRY   => "looks angry.",
            MoodEnum.INJURED => "looks injured.",
            MoodEnum.SICK    => "looks sick.",
            _                => "seems to be in their own world."
        };
    }
}
