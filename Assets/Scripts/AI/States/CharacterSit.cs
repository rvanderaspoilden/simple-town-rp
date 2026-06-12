using DG.Tweening;
using Interaction;
using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterSit : IState {
        private readonly PlayerController player;
        private readonly ISeatBehavior props;
        private readonly Transform seatTransform;
        private readonly Vector3 lastPosition;

        public CharacterSit(PlayerController player, ISeatBehavior props, Transform seatTransform) {
            this.player = player;
            this.props = props;
            this.seatTransform = seatTransform;
            this.lastPosition = this.player.transform.position;
        }

        public void OnEnter() {
            this.player.NavMeshAgent.enabled = false;
            this.player.Collider.enabled = false;
            this.player.PlayerState = PlayerState.SITTING;

            Transform characterTransform = this.player.transform;

            characterTransform.DOComplete(); // Do stop look at rotation

            characterTransform.position = seatTransform.position;
            characterTransform.rotation = seatTransform.rotation;

            this.player.SetAnimatorAction(CharacterAnimatorAction.SIT);

            this.player.SetHeadTargetPosition(this.player.SitHeadPosition);
        }

        public void Tick() { }

        public void OnExit() {
            this.player.SetAnimatorAction(CharacterAnimatorAction.NONE);
            this.player.SetHeadTargetPosition(this.player.IdleHeadPosition);

            this.props.RevokeSeat();

            this.player.transform.position = lastPosition;
            this.player.Collider.enabled = true;
            this.player.NavMeshAgent.enabled = true;
        }
    }
}