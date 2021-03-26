using Sim;
using Sim.Building;
using UnityEngine;

namespace AI.States {
    public class CharacterLookAt : IState {
        private readonly Character character;
        private readonly Transform targetTransform;

        public CharacterLookAt(Character character, Props target) {
            this.character = character;
            this.targetTransform = target.transform;
        }

        public void OnEnter() {
        }

        public void Tick() {
            this.character.LookAt(targetTransform);
        }

        public void OnExit() {
        }
    }
}