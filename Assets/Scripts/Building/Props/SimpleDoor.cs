using System.Collections.Generic;
using DG.Tweening;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Sim.Building {
    public class SimpleDoor : NetworkBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Vector3 openedLocalRotation = new Vector3(0, -90, 0);

        [SerializeField]
        private Transform doorBody;

        [SerializeField]
        private NavMeshObstacle navMeshObstacle;

        [Header("Debug")]
        [SerializeField]
        private List<Collider> colliderTriggered;

        [SyncVar(hook = nameof(OnStateChanged))]
        private bool isOpened;

        [SyncVar(hook = nameof(OnLockStateChanged))]
        private DoorLockState lockState;

        [SyncVar]
        private uint parentId;

        protected void Awake() {
            this.colliderTriggered = new List<Collider>();
        }

        public override void OnStartClient() {
            base.OnStartClient();

            AssignParent();
        }

        [Server]
        public void SetLockState(DoorLockState state) {
            lockState = state;
        }

        protected virtual void AssignParent() {
            if (parentId == 0) return;

            Vector3 position = this.transform.position;
            Quaternion rotation = this.transform.rotation;

            if (!isClientOnly) return;

            if (NetworkIdentity.spawned.ContainsKey(this.parentId)) {
                this.transform.SetParent(NetworkIdentity.spawned[this.parentId].transform);
                this.transform.localPosition = position;
                this.transform.localRotation = rotation;
            } else {
                Debug.LogError($"Parent identity not found for door {this.name}");
            }
        }

        public uint ParentId {
            get => parentId;
            set => parentId = value;
        }

        [Client]
        private void OnStateChanged(bool oldValue, bool newValue) {
            this.isOpened = newValue;

            this.doorBody.DOComplete();

            if (this.isOpened) {
                this.doorBody.DOLocalRotate(Quaternion.Euler(openedLocalRotation).eulerAngles, .3f);
            } else {
                this.doorBody.DOLocalRotate(Quaternion.Euler(0, 0, 0).eulerAngles, .3f);
            }
        }

        [Client]
        private void OnLockStateChanged(DoorLockState oldValue, DoorLockState newValue) {
            this.lockState = newValue;
            this.navMeshObstacle.enabled = this.lockState == DoorLockState.LOCKED;
            this.CheckState();
        }

        private void OnTriggerEnter(Collider other) {
            if (!isServer) {
                return;
            }

            if (other.gameObject.layer == LayerMask.NameToLayer("Player") && !this.colliderTriggered.Find(x => x == other)) {
                this.colliderTriggered.Add(other);
            }

            this.CheckState();
        }

        private void OnTriggerExit(Collider other) {
            if (!isServer) {
                return;
            }

            this.colliderTriggered.Remove(other);

            this.CheckState();
        }

        [Server]
        private void CheckState() {
            this.isOpened = this.lockState == DoorLockState.UNLOCKED && this.colliderTriggered.Count > 0;
            Debug.Log($"Door opened : {this.isOpened}");
        }
    }

    public enum DoorLockState : byte {
        LOCKED,
        UNLOCKED
    }
}