using Photon.Pun;
using Sim.Building;
using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public abstract class Teleporter : Props {
        [Header("Teleporter Settings")]
        [SerializeField] protected PlacesEnum destination;
        
        public override void Use() {
            NetworkManager.Instance.GoToRoom(destination);
        }

        public PlacesEnum GetDestination() {
            return this.destination;
        }

        public void SetDestination(PlacesEnum destination) {
            this.photonView.RPC("RPC_SetDestination", RpcTarget.AllBuffered, destination);
        }

        [PunRPC]
        public void RPC_SetDestination(PlacesEnum placesEnum) {
            this.destination = destination;
        }
    }   
}
