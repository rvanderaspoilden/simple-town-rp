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
///   - Owner (tenant of this apartment): LOCK action(s) from PropsConfig
///   - Others: RING action from PropsConfig
/// Inner doors (_displayedNumber == 0) have no interaction.
///
/// Both actions must be declared in the door's PropsConfig.Actions array.
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
    [SerializeField] private AudioClip   ringClip;

    private bool          _isOpen;
    private DoorLockState _lockState = DoorLockState.UNLOCKED;
    private int           _displayedNumber = -1;

    // ── IInteractable overrides ───────────────────────────────────────────────

    public override bool IsInteractable() {
        if (!enabled || !gameObject.activeInHierarchy) return false;
        return _displayedNumber > 0;
    }

    public override Action[] GetActions(bool withPriority = false) {
        if (!enabled || !gameObject.activeInHierarchy) return System.Array.Empty<Action>();
        if (_displayedNumber <= 0 || _builtActions == null) return System.Array.Empty<Action>();

        if (IsLocalPlayerOwner()) {
            // Door UNLOCKED → show LOCK; door LOCKED → show UNLOCK
            ActionTypeEnum wantedType = _lockState == DoorLockState.UNLOCKED
                ? ActionTypeEnum.LOCK
                : ActionTypeEnum.UNLOCK;
            return _builtActions.Where(a => a != null && a.Type == wantedType).ToArray();
        }

        return _builtActions.Where(a => a != null && a.Type == ActionTypeEnum.RING).ToArray();
    }

    // ── IPropBehaviour override ───────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        DoorState state = DoorState.Deserialize(payload);

        if (numberTxt != null && state.DoorNumber != _displayedNumber) {
            _displayedNumber = state.DoorNumber;
            numberTxt.text   = state.DoorNumber > 0 ? state.DoorNumber.ToString() : string.Empty;
        }

        if (_lockState != state.LockState) {
            _lockState = state.LockState;
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
