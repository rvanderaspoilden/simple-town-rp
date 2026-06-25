using Sim;

namespace AI.States {
    /// <summary>
    /// Déplacement clavier direct (ZQSD), relatif à la caméra — mode « third-person
    /// controller » expérimental qui coexiste avec le click-to-move. La logique de
    /// steering vit dans PlayerController.TickFreeMove (accès caméra + vitesses +
    /// perks) ; cet état ne fait qu'orchestrer l'entrée/sortie. Sans input directionnel,
    /// on retombe sur idle.
    /// </summary>
    public class CharacterFreeMove : IState {
        private readonly PlayerController player;

        public CharacterFreeMove(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.MOVING;

            // On purge un éventuel chemin de click-to-move pour que les deux pilotes de
            // l'agent ne se battent pas (l'un suit un chemin, l'autre fait du Move direct).
            if (this.player.NavMeshAgent.hasPath) this.player.NavMeshAgent.ResetPath();
        }

        public void Tick() {
            if (!this.player.TickFreeMove()) {
                this.player.Idle();
            }
        }

        public void OnExit() {
            this.player.Animator.SetVelocity(0f);
        }
    }
}
