using Sim;
using Sim.Building;

namespace AI.States {
    public class CharacterInteract : IState {
        
        private readonly PlayerController player;

        private Props interactedProps;

        public CharacterInteract(PlayerController player) {
            this.player = player;
            this.player.PlayerState = PlayerState.INTERACTING;
        }

        public Props InteractedProps {
            get => interactedProps;
            set => interactedProps = value;
        }

        public void OnEnter() {
        }

        public void Tick() {
        }

        public void OnExit() {
            this.interactedProps.StopInteraction();
        }
    }
}