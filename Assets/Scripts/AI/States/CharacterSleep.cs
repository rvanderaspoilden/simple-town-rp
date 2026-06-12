using System.Linq;
using DG.Tweening;
using Interaction;
using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterSleep : IState {
        private readonly PlayerController player;
        private readonly ISeatBehavior props;
        private readonly Transform couchTransform;
        private readonly Vector3 lastPosition;

        public CharacterSleep(PlayerController player, ISeatBehavior props, Transform couchTransform) {
            this.player = player;
            this.props = props;
            this.couchTransform = couchTransform;
            this.lastPosition = this.player.transform.position;
        }

        public void OnEnter() {
            this.player.NavMeshAgent.enabled = false;
            this.player.Collider.enabled = false;
            this.player.PlayerState = PlayerState.SLEEPING;

            Transform characterTransform = this.player.transform;
            
            characterTransform.DOComplete(); // Do stop look at rotation

            characterTransform.position = couchTransform.position;
            characterTransform.rotation = couchTransform.rotation;

            this.player.SetAnimatorAction(CharacterAnimatorAction.SLEEP);

            this.player.SetHeadTargetPosition(this.player.SitHeadPosition);
            
            SubGameController.Instance.LaunchSubGame(DatabaseManager.SubGameConfigurations.First(x => x.SubGameType == SubGameType.DREAM));
        }

        public void Tick() { }

        public void OnExit() {
            SubGameController.Instance.StopSubGame();
            
            this.player.SetAnimatorAction(CharacterAnimatorAction.NONE);
            this.player.SetHeadTargetPosition(this.player.IdleHeadPosition);

            this.props.RevokeCouch();

            this.player.transform.position = lastPosition;
            this.player.Collider.enabled = true;
            this.player.NavMeshAgent.enabled = true;
        }
    }
}