using Sim.Building;
using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public abstract class Teleporter : Props {
        [Header("Teleporter Settings")]
        [SerializeField] private PlacesEnum destination;
        
        public override void Use() {
            NetworkManager.Instance.GoToRoom(destination);
        }
    }   
}
