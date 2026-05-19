using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterMove : IState {
        private readonly PlayerController player;

        public CharacterMove(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.MOVING;
        }

        public void Tick() {
            MarkerController.Instance.ShowAt(this.player.NavMeshAgent.pathEndPosition);

            // desiredVelocity (target velocity along the path) instead of velocity (smoothed by
            // agent acceleration): the animator parameter snaps 0 ↔ speed so the blend tree picks
            // exactly one of idle/walk/run instead of mixing all three during accel/decel.
            Vector3 desired = this.player.NavMeshAgent.desiredVelocity;
            this.player.Animator.SetVelocity(desired.magnitude);

            if (desired.sqrMagnitude > 0.0001f) {
                this.player.transform.rotation = Quaternion.LookRotation(desired.normalized);
            }
        }

        public void OnExit() {
            this.player.NavMeshAgent.ResetPath();

            MarkerController.Instance.Hide();
        }
    }
}