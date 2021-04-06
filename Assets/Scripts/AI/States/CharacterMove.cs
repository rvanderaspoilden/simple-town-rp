using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterMove : IState {
        private readonly PlayerController player;

        public CharacterMove(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            Debug.Log("Enter in Move state");
        }

        public void Tick() {
            MarkerController.Instance.ShowAt(this.player.NavMeshAgent.pathEndPosition);

            this.player.Animator.SetVelocity(this.player.NavMeshAgent.velocity.magnitude);

            this.player.transform.rotation = Quaternion.LookRotation(this.player.NavMeshAgent.velocity.normalized);
        }

        public void OnExit() {
            this.player.NavMeshAgent.ResetPath();

            MarkerController.Instance.Hide();
        }
    }
}