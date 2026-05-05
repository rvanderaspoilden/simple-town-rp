using DG.Tweening;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Client-side behaviour for Door props.
/// Receives DoorState updates from the server and animates the door body.
/// </summary>
public class DoorBehaviour : MonoBehaviour, IPropBehaviour {
    [Header("Door")]
    [SerializeField] private Transform       doorBody;
    [SerializeField] private Vector3         openedLocalRotation = new Vector3(0, -90, 0);
    [SerializeField] private NavMeshObstacle navMeshObstacle;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   openClip;
    [SerializeField] private AudioClip   closeClip;

    private bool _isOpen;

    public void ApplyState(PropType type, byte[] payload) {
        DoorState state = DoorState.Deserialize(payload);
        if (_isOpen == state.IsOpen) return;
        _isOpen = state.IsOpen;

        doorBody.DOComplete();

        if (_isOpen) {
            audioSource?.PlayOneShot(openClip);
            doorBody.DOLocalRotate(openedLocalRotation, .3f);
        } else {
            audioSource?.PlayOneShot(closeClip);
            doorBody.DOLocalRotate(Vector3.zero, .3f);
        }

        if (navMeshObstacle != null) navMeshObstacle.enabled = !_isOpen;
    }
}
