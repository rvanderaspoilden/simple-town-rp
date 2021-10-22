using Sim;

namespace AI.States {
    public class CharacterDie : IState {
        private readonly PlayerController player;

        public CharacterDie(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.Animator.SetAction(CharacterAnimatorAction.DIE);

            this.player.SetHeadTargetPosition(this.player.SitHeadPosition);
            
            HUDManager.Instance.CloseContextMenu();
            HUDManager.Instance.CloseInventory();
        }

        public void Tick() { }

        public void OnExit() {
            this.player.Animator.SetAction(CharacterAnimatorAction.NONE);
            this.player.SetHeadTargetPosition(this.player.IdleHeadPosition);
        }
    }
}