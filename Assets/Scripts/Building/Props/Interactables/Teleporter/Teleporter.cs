using Mirror;
using Sim.Building;
using Sim.Enums;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sim.Interactables {
    public class Teleporter : Props {
        [Header("Settings")]
        [SerializeField]
        private Transform spawnTransform;

        [SerializeField]
        [SyncVar]
        private bool goToMainHall;

        public delegate void UseEvent(Teleporter teleporter, int originFloor, int floorDestination, NetworkConnectionToClient playerConn);

        public event UseEvent OnUse;

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.TELEPORT)) {
                this.CmdUse();
            }
        }

        protected override void AssignParent() {
            if (parentId == 0) return;

            Vector3 position = this.transform.position;

            if (!isClientOnly) return;

            if (NetworkIdentity.spawned.ContainsKey(this.parentId)) {
                this.transform.SetParent(NetworkIdentity.spawned[this.parentId].transform);
                this.transform.localPosition = position;
            } else {
                Debug.LogError($"Parent identity not found for props {this.name}");
            }

        }

        public bool GOToMainHall {
            get => goToMainHall;
            set => goToMainHall = value;
        }

        public Transform SpawnTransform => spawnTransform;

        [Command(requiresAuthority = false)]
        public void CmdUse(NetworkConnectionToClient sender = null) {
            Debug.Log($"{sender.connectionId} want to go to hall number {1}");

            OnUse?.Invoke(this, goToMainHall ? GetComponentInParent<HallController>().FloorNumber : 0, goToMainHall ? 0 : 1, sender);
        }
    }
}