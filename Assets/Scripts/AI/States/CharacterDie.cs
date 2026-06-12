using Sim;
using Sim.Enums;

namespace AI.States {
    public class CharacterDie : IState {
        private readonly PlayerController player;

        public CharacterDie(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.DIED;

            this.player.SetAnimatorAction(CharacterAnimatorAction.DIE);

            this.player.SetHeadTargetPosition(this.player.SitHeadPosition);

            HUDManager.Instance.CloseContextMenu();
            HUDManager.Instance.CloseInventory();

            if (BuildManager.Instance.GetMode() != BuildModeEnum.NONE) {
                BuildManager.Instance.Cancel();
            }
        }

        public void Tick() { }

        public void OnExit() {
            this.player.SetAnimatorAction(CharacterAnimatorAction.NONE);
            this.player.SetHeadTargetPosition(this.player.IdleHeadPosition);
        }
    }
}