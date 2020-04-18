using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public class Teleporter : Interactable {
        [Header("Door Settings")]
        [SerializeField] private PlacesEnum destination;

        public override void Interact() {
            base.Interact();
        
            NetworkManager.Instance.GoToRoom(destination);
        }
    }   
}
