using System;
using Sim;
using Sim.Building;
using UnityEngine;

namespace AI.States {
    public class CharacterSit : IState {
        private readonly Player player;
        private readonly Seat props;
        private readonly Transform seatTransform;

        public CharacterSit(Player player, Seat props, Transform seatTransform) {
            this.player = player;
            this.props = props;
            this.seatTransform = seatTransform;
        }

        public void OnEnter() {
            this.player.NavMeshAgent.enabled = false;

            Transform playerTransform = this.player.transform;
            playerTransform.position = seatTransform.position;
            playerTransform.rotation = seatTransform.rotation;
            
            this.player.Animator.SetAction(CharacterAnimatorAction.SIT);
        }

        public void Tick() {
        }

        public void OnExit() {
            this.player.Animator.SetAction(CharacterAnimatorAction.NONE);
            this.props.RevokeSeatForPlayer(this.player);
            this.player.NavMeshAgent.enabled = true;
        }
    }
}