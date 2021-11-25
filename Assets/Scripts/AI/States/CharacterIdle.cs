using Sim;

namespace AI.States {
    public class CharacterIdle : IState {
        private readonly PlayerController player;

        public CharacterIdle(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.IDLE;

            if (this.player.PropsTarget && this.player.CanInteractWith(this.player.PropsTarget)) {
                this.player.LookAt(this.player.PropsTarget.transform);
                HUDManager.Instance.ShowContextMenu(this.player.PropsTarget.GetActions(this.player.ShowRadialMenuWithPriority), this.player.PropsTarget.transform, this.player.ShowRadialMenuWithPriority);
                this.player.PropsTarget = null;
            }
        }

        public void Tick() {
            this.player.Animator.SetVelocity(this.player.NavMeshAgent.velocity.magnitude);
        }

        public void OnExit() {
            HUDManager.Instance.CloseContextMenu();
            HUDManager.Instance.CloseInventory();
        }
    }
}