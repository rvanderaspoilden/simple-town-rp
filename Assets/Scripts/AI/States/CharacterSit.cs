using System;
using Sim;
using Sim.Building;
using UnityEngine;

namespace AI.States {
    public class CharacterSit : IState {
        private readonly Player player;
        private readonly Seat props;
        private readonly Transform seatTransform;
        private readonly Vector3 lastPosition;

        public CharacterSit(Player player, Seat props, Transform seatTransform) {
            this.player = player;
            this.props = props;
            this.seatTransform = seatTransform;
            this.lastPosition = this.player.transform.position;
        }

        public void OnEnter() {
            this.player.NavMeshAgent.enabled = false;
            this.player.Collider.enabled = false;

            Transform playerTransform = this.player.transform;
            playerTransform.position = seatTransform.position;
            playerTransform.rotation = seatTransform.rotation;

            this.player.Animator.SetAction(CharacterAnimatorAction.SIT);

            this.player.SetHeadTargetPosition(this.player.SitHeadPosition);
        }

        public void Tick() { }

        public void OnExit() {
            this.player.Animator.SetAction(CharacterAnimatorAction.NONE);
            this.player.SetHeadTargetPosition(this.player.IdleHeadPosition);

            this.props.RevokeSeatForPlayer(this.player);

            this.player.transform.position = lastPosition;
            this.player.Collider.enabled = true;
            this.player.NavMeshAgent.enabled = true;
        }
    }
}