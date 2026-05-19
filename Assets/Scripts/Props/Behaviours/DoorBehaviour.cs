using System.Collections.Generic;
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
///   Unlocked + owner      → [LOCK]
///   Unlocked + non-owner  → []                   (door is walk-through, left-click navigates past it)
///   Locked   + owner      → [UNLOCK, RING]
///   Locked   + non-owner  → [RING]
///
/// Right-click-only is dynamic: TRUE when unlocked (so left-click rays pass through for navigation),
/// FALSE when locked (so left-click also triggers the priority action — UNLOCK for owner, RING for others).
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

    [Header("Roof Reveal")]
    [Tooltip("Optional. If set, the trigger is opened whenever the door is open (front doors only). " +
             "Standing in its volume hides the parent building's Roof, even from outside.")]
    [SerializeField] private RoofRevealTrigger roofRevealTrigger;

    private bool          _isOpen;
    private DoorLockState _lockState = DoorLockState.UNLOCKED;
    private int           _displayedNumber = -1;

    // ── IInteractable overrides ───────────────────────────────────────────────

    public override bool IsInteractable() {
        if (!enabled || !gameObject.activeInHierarchy || _displayedNumber <= 0) return false;
        return GetActions().Length > 0;
    }

    public override bool IsRightClickOnly() => _lockState == DoorLockState.UNLOCKED;

    public override Action[] GetActions(bool withPriority = false) {
        if (!enabled || !gameObject.activeInHierarchy) return System.Array.Empty<Action>();
        if (_displayedNumber <= 0 || _builtActions == null) return System.Array.Empty<Action>();

        bool isOwner  = IsLocalPlayerOwner();
        bool isLocked = _lockState == DoorLockState.LOCKED;
        var result = new List<Action>();

        if (isLocked) {
            if (isOwner) result.AddRange(_builtActions.Where(a => a != null && a.Type == ActionTypeEnum.UNLOCK));
            if(!isOwner) result.AddRange(_builtActions.Where(a => a != null && a.Type == ActionTypeEnum.RING));
        } else if (isOwner) {
            result.AddRange(_builtActions.Where(a => a != null && a.Type == ActionTypeEnum.LOCK));
        }

        return result.ToArray();
    }

    // ── IPropBehaviour override ───────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        DoorState state = DoorState.Deserialize(payload);

        if (state.DoorNumber != _displayedNumber) {
            _displayedNumber = state.DoorNumber;
            if (numberTxt != null) {
                numberTxt.text = state.DoorNumber > 0 ? state.DoorNumber.ToString() : string.Empty;
            }
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

        // Front doors (_displayedNumber > 0) reveal the apartment roof while open.
        // Inner doors keep the gate closed regardless of their open state.
        if (roofRevealTrigger != null) {
            roofRevealTrigger.GateOpen = _isOpen && _displayedNumber > 0;
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
