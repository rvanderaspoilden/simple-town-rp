using Interaction;
using Sim;
using Sim.Building;

namespace AI.States {
    public class CharacterInteract : IState {
        
        private readonly PlayerController player;

        private IInteractable interactable;

        public CharacterInteract(PlayerController player) {
            this.player = player;
            this.player.PlayerState = PlayerState.INTERACTING;
        }

        public IInteractable Interactable {
            get => interactable;
            set {
                interactable = value;
                this.player.LookAt(interactable.transform);
            }
        }

        public void OnEnter() {
            if (interactable != null) {
                this.player.LookAt(interactable.transform);
            }
        }

        public void Tick() {
        }

        public void OnExit() {
            this.interactable.StopInteraction();
        }
    }
}