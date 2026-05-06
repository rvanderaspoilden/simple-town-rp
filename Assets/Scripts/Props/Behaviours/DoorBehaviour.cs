using DG.Tweening;
using Sim.Building;
using Sim.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Client-side behaviour for Door props.
/// Receives DoorState updates from the server and animates the door body.
/// Optional numberTxt is set on FrontDoor prefabs to display the apartment number.
/// </summary>
public class DoorBehaviour : PropBehaviourBase {
    [Header("Door")]
    [SerializeField] private Transform       doorBody;
    [SerializeField] private Vector3         openedLocalRotation = new Vector3(0, -90, 0);
    [SerializeField] private NavMeshObstacle navMeshObstacle;
    [SerializeField] private TextMeshPro     numberTxt;   // optional, only assigned on FrontDoor prefab

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   openClip;
    [SerializeField] private AudioClip   closeClip;

    private bool          _isOpen;
    private DoorLockState _lockState = DoorLockState.UNLOCKED;
    private int           _displayedNumber = -1;

    public override void ApplyState(PropType type, byte[] payload) {
        DoorState state = DoorState.Deserialize(payload);

        // ── Number display (front doors only) ─────────────────────────────────
        if (numberTxt != null && state.DoorNumber != _displayedNumber) {
            _displayedNumber = state.DoorNumber;
            numberTxt.text   = state.DoorNumber > 0 ? state.DoorNumber.ToString() : string.Empty;
        }

        // ── Lock state ────────────────────────────────────────────────────────
        // The door is "blocked" (navmesh obstacle on) when LOCKED OR closed.
        // When UNLOCKED and open, the obstacle is off so NPCs/players can pass.
        if (_lockState != state.LockState) {
            _lockState = state.LockState;
        }

        // ── Open/close animation ──────────────────────────────────────────────
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

        // NavMesh: enabled when LOCKED (always blocks) OR when CLOSED.
        // Disabled when UNLOCKED + OPEN.
        if (navMeshObstacle != null) {
            navMeshObstacle.enabled = _lockState == DoorLockState.LOCKED || !_isOpen;
        }
    }
}
