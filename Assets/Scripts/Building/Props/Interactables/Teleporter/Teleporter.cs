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

        private HallController hallController;

        public delegate void UseEvent(Teleporter teleporter, int originFloor, int floorDestination, NetworkConnectionToClient playerConn);

        public event UseEvent OnUse;

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.TELEPORT)) {
                PlayerController.Local.Interact(this);
                DefaultViewUI.Instance.ShowElevatorUI(this);
            }
        }

        protected override void AssignParent() {
            if (parentId == 0) return;

            Vector3 position = this.transform.position;

            if (!isClientOnly) return;

            if (NetworkIdentity.spawned.ContainsKey(this.parentId)) {
                this.hallController = NetworkIdentity.spawned[this.parentId].GetComponent<HallController>();
                this.transform.SetParent(this.hallController.transform);
                this.transform.localPosition = position;
            } else {
                Debug.LogError($"Parent identity not found for props {this.name}");
            }
        }

        public override void StopInteraction() {
            DefaultViewUI.Instance.HideElevatorUI();
        }

        public Transform SpawnTransform => spawnTransform;

        [Command(requiresAuthority = false)]
        public void CmdUse(int floorDestination, NetworkConnectionToClient sender = null) {
            Debug.Log($"Server: Player {sender.identity.netId} want to go to floor {floorDestination}");
            this.hallController = GetComponentInParent<HallController>();

            int originFloor = this.hallController ? this.hallController.FloorNumber : 0;

            if (originFloor != floorDestination) {
                OnUse?.Invoke(this, originFloor, floorDestination, sender);
            }
        }
    }
}