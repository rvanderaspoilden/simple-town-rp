using System;
using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterMove : IState {
        private readonly Player player;

        public CharacterMove(Player player) {
            this.player = player;
        }

        public void OnEnter() {
            Debug.Log("Enter in Move state");
        }

        public void Tick() {
            MarkerController.Instance.ShowAt(this.player.NavMeshAgent.pathEndPosition);

            this.player.Animator.SetVelocity(this.player.NavMeshAgent.velocity.magnitude);
        }

        public void OnExit() {
            if (this.player.PropsTarget && this.player.CanInteractWith(this.player.PropsTarget)) {
                HUDManager.Instance.DisplayContextMenu(true,
                    CameraManager.camera.WorldToScreenPoint(this.player.PropsTarget.transform.position), this.player.PropsTarget);
                this.player.PropsTarget = null;
            }
            
            this.player.NavMeshAgent.ResetPath();
            
            MarkerController.Instance.Hide();


        }
    }
}