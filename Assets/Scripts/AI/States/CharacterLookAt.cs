using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterLookAt : IState {
        private readonly PlayerController player;
        private Transform target;

        public CharacterLookAt(PlayerController player) {
            this.player = player;
            this.player.PlayerState = PlayerState.LOOKING;
        }

        public Transform Target {
            get => target;
            set => target = value;
        }

        public void OnEnter() {
        }

        public void Tick() {
            this.player.LookAt(target);
        }

        public void OnExit() {
        }
    }
}