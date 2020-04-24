using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public class Teleporter : Interactable {
        [Header("Door Settings")]
        [SerializeField] private PlacesEnum destination;

        protected override void SetupActions() {
            this.actions = new Action[1] {
                new Action(ActionTypeEnum.USE, "Rentrer")
            };
        }
        
        public override void Use() {
            NetworkManager.Instance.GoToRoom(destination);
        }
    }   
}
