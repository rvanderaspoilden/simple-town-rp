using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterIdle : IState {
        private readonly Character character;

        public CharacterIdle(Character character) {
            this.character = character;
        }

        public void OnEnter() {
            Debug.Log("Enter in Idle state");
        }

        public void Tick() {
            this.character.Animator.SetVelocity(this.character.NavMeshAgent.velocity.magnitude);
        }

        public void OnExit() { }
    }
}