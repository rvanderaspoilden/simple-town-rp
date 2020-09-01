using Sim.Interactables;
using UnityEngine;

namespace Sim.Building {
    public class Seat : Props {
        
        /*[Header("Seat settings")]
        [SerializeField] private Action useAction;

        protected override void SetupActions() {
            this.actions = new Action[1] {this.useAction};
        }*/

        public override void Use() {
            // todo s'asseoir
        }
    }
}