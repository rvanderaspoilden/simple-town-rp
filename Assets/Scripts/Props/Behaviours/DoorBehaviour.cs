using System.Linq;
using DG.Tweening;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for Door props.
/// Receives DoorState updates from the server and animates the door body.
/// Optional numberTxt is set on FrontDoor prefabs to display the apartment number.
///
/// Interaction rules (front door only, _displayedNumber > 0):
///   - Owner (tenant of this apartment): LOCK / UNLOCK toggle (from PropsConfig.Actions)
///   - Others: RING (from PropsConfig.Actions)
/// Inner doors (_displayedNumber == 0) have no interaction.
/// </summary>
public class DoorBehaviour : PropBehaviourBase {
    [Header("Door")]
    [SerializeField] private Transform       doorBody;
    [SerializeField] private Vector3         openedLocalRotation = new Vector3(0, -90, 0);
    [SerializeField] private NavMeshObstacle navMeshObstacle;
    [SerializeField] private TextMeshPro     numberTxt;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   openClip;
    [SerializeField] private AudioClip   closeClip;
    [SerializeField] private AudioClip   lockClip;
    [SerializeField] private AudioClip   unlockClip;
    [SerializeField] private AudioClip   ringClip;

    private bool          _isOpen;
    private DoorLockState _lockState = DoorLockState.UNLOCKED;
    private int           _displayedNumber = -1;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake() {
        base.Awake();
        Debug.Log($"[DoorBehaviour] {name} Awake — builtActions={_builtActions?.Length ?? 0} types=[{(_builtActions != null ? string.Join(",", _builtActions.Where(a => a != null).Select(a => a.Type.ToString())) : "")}]");
    }

    // ── IInteractable overrides ───────────────────────────────────────────────

    public override bool IsInteractable() {
        bool interactable = enabled && gameObject.activeInHierarchy && _displayedNumber > 0;
        Debug.Log($"[DoorBehaviour] {name} IsInteractable={interactable} (enabled={enabled} active={gameObject.activeInHierarchy} displayedNumber={_displayedNumber})");
        return interactable;
    }

    public override Action[] GetActions(bool withPriority = false) {
        if (!enabled || !gameObject.activeInHierarchy) return System.Array.Empty<Action>();
        if (_displayedNumber <= 0 || _builtActions == null) {
            Debug.Log($"[DoorBehaviour] {name} GetActions skipped: displayedNumber={_displayedNumber} builtActionsCount={_builtActions?.Length ?? 0}");
            return System.Array.Empty<Action>();
        }

        bool isOwner = IsLocalPlayerOwner();
        Debug.Log($"[DoorBehaviour] {name} GetActions: displayedNumber={_displayedNumber} isOwner={isOwner} lockState={_lockState} builtActions=[{string.Join(", ", _builtActions.Where(a => a != null).Select(a => a.Type.ToString()))}]");

        if (isOwner) {
            ActionTypeEnum wantedType = _lockState == DoorLockState.UNLOCKED
                ? ActionTypeEnum.LOCK
                : ActionTypeEnum.UNLOCK;
            Action[] filtered = _builtActions.Where(a => a != null && a.Type == wantedType).ToArray();
            if (filtered.Length == 0 && wantedType == ActionTypeEnum.UNLOCK)
                filtered = _builtActions.Where(a => a != null && a.Type == ActionTypeEnum.LOCK).ToArray();
            return filtered;
        }

        return _builtActions.Where(a => a != null && a.Type == ActionTypeEnum.RING).ToArray();
    }

    // ── IPropBehaviour override ───────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        Debug.Log($"[DoorBehaviour] {name} ApplyState called payloadLen={payload?.Length ?? 0}");
        DoorState state = DoorState.Deserialize(payload);

        if (state.DoorNumber != _displayedNumber) {
            _displayedNumber = state.DoorNumber;
            if (numberTxt != null) {
                numberTxt.text = state.DoorNumber > 0 ? state.DoorNumber.ToString() : string.Empty;
            }
            Debug.Log($"[DoorBehaviour] {name} doorNumber updated → {_displayedNumber}");
        }

        if (_lockState != state.LockState) {
            _lockState = state.LockState;
            if (audioSource != null) {
                AudioClip clip = _lockState == DoorLockState.LOCKED ? lockClip : unlockClip;
                if (clip != null) audioSource.PlayOneShot(clip);
            }
        }

        if (_isOpen != state.IsOpen) {
            _isOpen = state.IsOpen;
            doorBody?.DOComplete();

            if (_isOpen) {
                if (audioSource != null && openClip != null) audioSource.PlayOneShot(openClip);
                doorBody?.DOLocalRotate(openedLocalRotation, .3f);
            } else {
                if (audioSource != null && closeClip != null) audioSource.PlayOneShot(closeClip);
                doorBody?.DOLocalRotate(Vector3.zero, .3f);
            }
        }

        if (navMeshObstacle != null) {
            navMeshObstacle.enabled = _lockState == DoorLockState.LOCKED || !_isOpen;
        }
    }

    // ── Ring sound (called by ClientPropManager on S2C_DoorRing) ─────────────

    public void PlayRingSound() {
        if (audioSource != null && ringClip != null)
            audioSource.PlayOneShot(ringClip);
    }

    // ── PropBehaviourBase hook ────────────────────────────────────────────────

    protected override void Execute(Action action) {
        switch (action.Type) {
            case ActionTypeEnum.LOCK:
            case ActionTypeEnum.UNLOCK:
                SendPropInteraction(PropType.Door, DoorInteraction.LockRequest);
                break;
            case ActionTypeEnum.RING:
                SendPropInteraction(PropType.Door, DoorInteraction.RingRequest);
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsLocalPlayerOwner() {
        Home home = Sim.PlayerController.Local?.CharacterHome;
        return home != null && home.Address.doorNumber == _displayedNumber;
    }
}
