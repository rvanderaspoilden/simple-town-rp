using Sim;
using Sim.Building;
using UnityEngine;

namespace AI.States {
    public class CharacterSit : IState {
        private readonly Character character;
        private readonly Seat props;
        private readonly Transform seatTransform;
        private readonly Vector3 lastPosition;

        public CharacterSit(Character character, Seat props, Transform seatTransform) {
            this.character = character;
            this.props = props;
            this.seatTransform = seatTransform;
            this.lastPosition = this.character.transform.position;
        }

        public void OnEnter() {
            this.character.NavMeshAgent.enabled = false;
            this.character.Collider.enabled = false;

            Transform characterTransform = this.character.transform;
            characterTransform.position = seatTransform.position;
            characterTransform.rotation = seatTransform.rotation;

            this.character.Animator.SetAction(CharacterAnimatorAction.SIT);

            this.character.SetHeadTargetPosition(this.character.SitHeadPosition);
        }

        public void Tick() { }

        public void OnExit() {
            this.character.Animator.SetAction(CharacterAnimatorAction.NONE);
            this.character.SetHeadTargetPosition(this.character.IdleHeadPosition);

            this.props.RevokeSeat(this.character);

            this.character.transform.position = lastPosition;
            this.character.Collider.enabled = true;
            this.character.NavMeshAgent.enabled = true;
        }
    }
}