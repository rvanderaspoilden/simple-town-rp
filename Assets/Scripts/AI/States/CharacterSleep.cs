using DG.Tweening;
using Sim;
using Sim.Building;
using UnityEngine;

namespace AI.States {
    public class CharacterSleep : IState {
        private readonly Character character;
        private readonly Seat props;
        private readonly Transform couchTransform;
        private readonly Vector3 lastPosition;

        public CharacterSleep(Character character, Seat props, Transform couchTransform) {
            this.character = character;
            this.props = props;
            this.couchTransform = couchTransform;
            this.lastPosition = this.character.transform.position;
        }

        public void OnEnter() {
            this.character.NavMeshAgent.enabled = false;
            this.character.Collider.enabled = false;

            Transform characterTransform = this.character.transform;
            
            characterTransform.DOComplete(); // Do stop look at rotation

            characterTransform.position = couchTransform.position;
            characterTransform.rotation = couchTransform.rotation;

            this.character.Animator.SetAction(CharacterAnimatorAction.SLEEP);

            this.character.SetHeadTargetPosition(this.character.SitHeadPosition);
        }

        public void Tick() { }

        public void OnExit() {
            this.character.Animator.SetAction(CharacterAnimatorAction.NONE);
            this.character.SetHeadTargetPosition(this.character.IdleHeadPosition);

            this.props.RevokeCouch(this.character);

            this.character.transform.position = lastPosition;
            this.character.Collider.enabled = true;
            this.character.NavMeshAgent.enabled = true;
        }
    }
}