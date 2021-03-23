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
            this.character.NavMeshAgent.ResetPath();

            MarkerController.Instance.Hide();
        }
    }
}