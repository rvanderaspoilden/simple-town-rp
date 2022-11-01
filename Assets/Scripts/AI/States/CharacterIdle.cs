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

            if (this.player.InteractableTarget == null ||
                !this.player.CanInteractWith(this.player.InteractableTarget, this.player.InteractableTarget.transform.position)) return;
            
            Transform interactableTransform = this.player.InteractableTarget.transform;
            this.player.LookAt(interactableTransform);
            HUDManager.Instance.ShowContextMenu(
                this.player.InteractableTarget.GetActions(this.player.ShowRadialMenuWithPriority),
                interactableTransform,
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