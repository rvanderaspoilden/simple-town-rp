using System.Collections.Generic;
using DG.Tweening;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Sim.Building {
    /// <summary>
    /// Trigger-based auto-opening door for the City scene.
    /// Has no NetworkIdentity of its own — state is owned by CityPropsStateManager.
    /// Assign a unique propId in the Inspector for each city door instance.
    /// </summary>
    public class CityDoor : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private int propId;
        [SerializeField] private Vector3 openedLocalRotation = new Vector3(0, -90, 0);
        [SerializeField] private DoorLockState defaultLockState = DoorLockState.UNLOCKED;
        [SerializeField] private Transform doorBody;
        [SerializeField] private NavMeshObstacle navMeshObstacle;
        [SerializeField] private AudioClip doorOpenSound;
        [SerializeField] private AudioClip doorCloseSound;
        [SerializeField] private AudioSource audioSource;

        private readonly List<Collider> collidersInTrigger = new List<Collider>();
        private bool isOpen;

        public DoorLockState DefaultLockState => defaultLockState;

        private void Start() {
            if (navMeshObstacle) navMeshObstacle.enabled = defaultLockState == DoorLockState.LOCKED;
            CityPropsStateManager.Instance?.RegisterDoor(propId, this);
        }

        private void OnTriggerEnter(Collider other) {
            if (!NetworkServer.active) return;
            if (other.gameObject.layer != LayerMask.NameToLayer("Player")) return;
            if (!collidersInTrigger.Contains(other)) collidersInTrigger.Add(other);
            CityPropsStateManager.Instance?.ServerDoorTriggersChanged(propId, collidersInTrigger.Count > 0, defaultLockState);
        }

        private void OnTriggerExit(Collider other) {
            if (!NetworkServer.active) return;
            collidersInTrigger.RemoveAll(c => c == null || c == other);
            CityPropsStateManager.Instance?.ServerDoorTriggersChanged(propId, collidersInTrigger.Count > 0, defaultLockState);
        }

        public void ApplyOpenState(bool open) {
            if (isOpen == open) return;
            isOpen = open;
            doorBody.DOComplete();
            if (open) {
                audioSource?.PlayOneShot(doorOpenSound);
                doorBody.DOLocalRotate(openedLocalRotation, .3f);
            } else {
                audioSource?.PlayOneShot(doorCloseSound);
                doorBody.DOLocalRotate(Vector3.zero, .3f);
            }
        }
    }
}
