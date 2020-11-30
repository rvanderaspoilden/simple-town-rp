using Photon.Pun;
using Sim.Building;
using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public abstract class Teleporter : Props {
        [Header("Teleporter Settings")]
        [SerializeField] protected PlacesEnum destination;
        
        protected override void Use() {
            NetworkManager.Instance.GoToRoom(destination);
        }

        public override void Synchronize(Photon.Realtime.Player playerTarget) {
            base.Synchronize(playerTarget);
            
            this.SetDestination(this.destination, playerTarget);
        }

        public PlacesEnum GetDestination() {
            return this.destination;
        }

        public void SetDestination(PlacesEnum destination, RpcTarget rpcTarget) {
            this.photonView.RPC("RPC_SetDestination", rpcTarget, destination);
        }
        
        private void SetDestination(PlacesEnum destination, Photon.Realtime.Player playerTarget) {
            this.photonView.RPC("RPC_SetDestination", playerTarget, destination);
        }

        [PunRPC]
        public void RPC_SetDestination(PlacesEnum placesEnum) {
            this.destination = placesEnum;
        }
    }   
}
