using Photon.Pun;
using Photon.Realtime;
using Sim.Building;
using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public abstract class Teleporter : Props {
        [Header("Teleporter Settings")]
        [SerializeField]
        protected RoomTypeEnum destination;

        [SerializeField]
        [Tooltip("Represent the position where the player will be spawned after use")]
        private Transform spawnTransform;

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.TELEPORT)) {
                //NetworkManager.Instance.GoToRoom(RoomTypeEnum.BUILDING_HALL, null);
            }
        }
        
        public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);

            this.SetDestination(this.destination, playerTarget);
        }

        public RoomTypeEnum GetDestination() {
            return this.destination;
        }

        public Transform Spawn => spawnTransform;

        public void SetDestination(RoomTypeEnum destination, RpcTarget rpcTarget) { // TODO: look this
            this.photonView.RPC("RPC_SetDestination", rpcTarget, destination);
        }

        private void SetDestination(RoomTypeEnum destination, Player playerTarget) {
            this.photonView.RPC("RPC_SetDestination", playerTarget, destination);
        }

        [PunRPC]
        public void RPC_SetDestination(RoomTypeEnum placesEnum) {
            this.destination = placesEnum;
        }
    }
}