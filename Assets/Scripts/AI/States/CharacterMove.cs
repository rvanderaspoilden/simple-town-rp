using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterMove : IState {
        private readonly Character character;

        public CharacterMove(Character character) {
            this.character = character;
        }

        public void OnEnter() {
            Debug.Log("Enter in Move state");
        }

        public void Tick() {
            MarkerController.Instance.ShowAt(this.character.NavMeshAgent.pathEndPosition);

            this.character.Animator.SetVelocity(this.character.NavMeshAgent.velocity.magnitude);

            this.character.transform.rotation = Quaternion.LookRotation(this.character.NavMeshAgent.velocity.normalized);
        }

        public void OnExit() {
            if (this.character.PropsTarget && this.character.CanInteractWith(this.character.PropsTarget)) {
                HUDManager.Instance.DisplayContextMenu(true,
                    CameraManager.Instance.Camera.WorldToScreenPoint(this.character.PropsTarget.transform.position), this.character.PropsTarget);
                this.character.PropsTarget = null;
            }

            this.character.NavMeshAgent.ResetPath();

            MarkerController.Instance.Hide();
        }
    }
}