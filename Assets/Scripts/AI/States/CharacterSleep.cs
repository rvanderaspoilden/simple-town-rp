using DG.Tweening;
using Sim;
using Sim.Building;
using UnityEngine;

namespace AI.States {
    public class CharacterSleep : IState {
        private readonly PlayerController player;
        private readonly Seat props;
        private readonly Transform couchTransform;
        private readonly Vector3 lastPosition;

        public CharacterSleep(PlayerController player, Seat props, Transform couchTransform) {
            this.player = player;
            this.props = props;
            this.couchTransform = couchTransform;
            this.lastPosition = this.player.transform.position;
        }

        public void OnEnter() {
            this.player.NavMeshAgent.enabled = false;
            this.player.Collider.enabled = false;

            Transform characterTransform = this.player.transform;
            
            characterTransform.DOComplete(); // Do stop look at rotation

            characterTransform.position = couchTransform.position;
            characterTransform.rotation = couchTransform.rotation;

            this.player.Animator.SetAction(CharacterAnimatorAction.SLEEP);

            this.player.SetHeadTargetPosition(this.player.SitHeadPosition);
        }

        public void Tick() { }

        public void OnExit() {
            this.player.Animator.SetAction(CharacterAnimatorAction.NONE);
            this.player.SetHeadTargetPosition(this.player.IdleHeadPosition);

            this.props.RevokeCouch();

            this.player.transform.position = lastPosition;
            this.player.Collider.enabled = true;
            this.player.NavMeshAgent.enabled = true;
        }
    }
}