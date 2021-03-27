using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterLookAt : IState {
        private readonly Character character;
        private Transform target;

        public CharacterLookAt(Character character) {
            this.character = character;
        }

        public Transform Target {
            get => target;
            set => target = value;
        }

        public void OnEnter() {
        }

        public void Tick() {
            this.character.LookAt(target);
        }

        public void OnExit() {
        }
    }
}