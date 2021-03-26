using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterIdle : IState {
        private readonly Character character;

        public CharacterIdle(Character character) {
            this.character = character;
        }

        public void OnEnter() {
            if (this.character.PropsTarget && this.character.CanInteractWith(this.character.PropsTarget)) {
                this.character.LookAt(this.character.PropsTarget.transform);
                HUDManager.Instance.DisplayContextMenu(true, this.character.PropsTarget, this.character.ShowRadialMenuWithPriority);
                this.character.PropsTarget = null;
            }
        }

        public void Tick() {
            this.character.Animator.SetVelocity(this.character.NavMeshAgent.velocity.magnitude);
        }

        public void OnExit() {
            HUDManager.Instance.DisplayContextMenu(false);
        }
    }
}