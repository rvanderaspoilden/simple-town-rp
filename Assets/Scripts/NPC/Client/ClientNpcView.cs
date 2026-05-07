using System;
using Sim;
using Sim.Enums;
using Sim.Logging;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vue locale d'un NPC côté client. Reçoit des snapshots discrets et interpole
/// linéairement avec un léger délai pour absorber le jitter.
///
/// Pas de gameplay/réseau ici :
///   - Aucun NavMeshAgent côté client.
///   - Aucun rigidbody / physique.
///   - Aucune logique IA.
///
/// Réutilise le pipeline visuel du joueur :
///   - <see cref="CharacterStyleSetup"/> pour appliquer l'apparence (Style sérialisé).
///   - <see cref="PlayerAnimator"/> pour piloter l'Animator avec exactement les
///     mêmes paramètres que le joueur (Velocity / MoodType). Aucune duplication.
/// </summary>
public class ClientNpcView : MonoBehaviour {
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

    /// <summary>Dernier state logique reçu — pilote l'animation et le mode de rendu.</summary>
    private NpcStateType _currentState = NpcStateType.Idle;
    private NpcStateType _appliedState = (NpcStateType)255;

    private PlayerAnimator      _animator;
    private CharacterStyleSetup _styleSetup;

    public int      NpcId  { get; private set; }
    public string   RoomId { get; private set; }
    public string   FirstName { get; private set; }
    public string   LastName  { get; private set; }
    public MoodEnum Mood      { get; private set; }
    public string   FullName => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";

    private void Awake() {
        _animator   = GetComponent<PlayerAnimator>();
        _styleSetup = GetComponent<CharacterStyleSetup>();
    }

    /// <summary>Initialise le NPC à la réception d'un S2C_SpawnNpc.</summary>
    public void Init(int npcId, string roomId, string styleJson,
                     string firstName, string lastName, byte mood) {
        NpcId     = npcId;
        RoomId    = roomId;
        FirstName = firstName ?? string.Empty;
        LastName  = lastName  ?? string.Empty;
        Mood      = (MoodEnum)mood;

        // Apparence — même chemin que PlayerController.ParseCharacterData.
        if (_styleSetup != null && !string.IsNullOrEmpty(styleJson)) {
            try {
                Style style = JsonUtility.FromJson<Style>(styleJson);
                _styleSetup.ApplyStyle(style);
            }
            catch (Exception e) {
                Debug.LogWarning($"[ClientNpcView] Failed to apply style for npc#{npcId}: {e.Message}");
            }
        }

        // Mood — même chemin que PlayerController.SetMood (cast int → float dans PlayerAnimator).
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

        // Sitting : on snap directement à la position du siège (pas d'interpolation
        // de marche — sinon le NPC "glisse" vers le siège).
        bool isSitting   = state == NpcStateType.Sitting;
        bool snapJump    = _hasNext && (position - _next.Position).sqrMagnitude > snapDistance * snapDistance;

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

        // Quand assis on force la vélocité affichée à 0 pour que le blend tree
        // ne tente pas de jouer Walk en parallèle.
        _displayedSpeed = isSitting ? 0f : velocity.magnitude;
    }

    private void Update() {
        if (!_hasPrev || !_hasNext) return;

        // Position / rotation
        if (_currentState == NpcStateType.Sitting) {
            // Pas d'interpolation : on reste collé au siège (déjà appliqué dans PushSnapshot).
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

        // Animator — pilotage via PlayerAnimator (mêmes paramètres que le joueur).
        if (_animator != null) {
            _animator.SetVelocity(_displayedSpeed);

            // Sit / unsit déclenchés sur transition de state, pas en continu.
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
}
