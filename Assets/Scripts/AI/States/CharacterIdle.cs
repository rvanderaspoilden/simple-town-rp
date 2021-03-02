using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterIdle : IState {
        private readonly Player player;

        public CharacterIdle(Player player) {
            this.player = player;
        }

        public void OnEnter() {
            //this.player.NavMeshAgent.enabled = false;
            //this.player.Animator.SetVelocity(0);
            Debug.Log("Enter in Idle state");
        }

        public void Tick() {
            this.player.Animator.SetVelocity(this.player.NavMeshAgent.velocity.magnitude);
        }

        public void OnExit() {
            //this.player.NavMeshAgent.enabled = true;
        }
    }
}