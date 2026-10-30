using Interaction;
using Sim;
using Sim.Building;
using Sim.Utils;

namespace AI.States {
    public class CharacterInteract : IState {

        private readonly PlayerController player;

        private IInteractable interactable;

        public CharacterInteract(PlayerController player) {
            this.player = player;
        }

        public IInteractable Interactable {
            get => interactable;
            set {
                interactable = value;
                if (value.IsAlive()) {
                    this.player.LookAt(interactable.transform);
                }
            }
        }

        public void OnEnter() {
            // Posé à CHAQUE entrée (pas dans le constructeur, qui ne tourne qu'à l'init du
            // state machine) : CameraManager s'appuie dessus pour couper prompt/outline/E
            // pendant l'interaction.
            this.player.PlayerState = PlayerState.INTERACTING;

            if (interactable.IsAlive()) {
                this.player.LookAt(interactable.transform);
            }
        }

        public void Tick() {
        }

        public void OnExit() {
            if (interactable.IsAlive()) {
                this.interactable.StopInteraction();
            }
            this.interactable = null;
        }
    }
}
