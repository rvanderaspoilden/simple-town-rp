using Sim;
using Sim.Utils;
using UnityEngine;

namespace AI.States {
    public class CharacterIdle : IState {
        private readonly PlayerController player;

        public CharacterIdle(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.IDLE;

            var target = this.player.InteractableTarget;
            if (!target.IsAlive() || !this.player.CanInteractWith(target, this.player.InteractionOriginPoint)) {
                this.player.InteractableTarget = null;
                return;
            }

            this.player.LookAt(target.transform);
            HUDManager.Instance.ShowContextMenu(
                target.GetActions(this.player.ShowRadialMenuWithPriority),
                target.transform,
                this.player.ShowRadialMenuWithPriority
            );
            this.player.InteractableTarget = null;
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