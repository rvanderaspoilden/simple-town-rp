using UnityEngine;

namespace Sim {
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour {
        private Animator animator;

        private int velocityHash;
        private int moodHash;
        private int actionHash;
        private int rightHandPoseHash;
        private int leftHandPoseHash;
        private int twoHandPoseHash;

        private void Awake() {
            this.animator = GetComponent<Animator>();
            this.velocityHash = Animator.StringToHash("Velocity");
            this.moodHash = Animator.StringToHash("MoodType");
            this.actionHash = Animator.StringToHash("Action");
            this.rightHandPoseHash = Animator.StringToHash("RightHandPose");
            this.leftHandPoseHash = Animator.StringToHash("LeftHandPose");
            this.twoHandPoseHash = Animator.StringToHash("TwoHandPose");
        }

        public void SetVelocity(float value) {
            this.animator.SetFloat(velocityHash, value);
        }

        public void SetMood(float value) {
            this.animator.SetFloat(moodHash, value);
        }

        public void SetAction(CharacterAnimatorAction action) {
            this.animator.SetFloat(actionHash, (float)action);
        }

        public void SetRightHandPose(HandPose pose) {
            this.animator.SetInteger(rightHandPoseHash, (int)pose);
        }

        public void SetLeftHandPose(HandPose pose) {
            this.animator.SetInteger(leftHandPoseHash, (int)pose);
        }

        public void SetTwoHandPose(TwoHandPose pose) {
            this.animator.SetInteger(twoHandPoseHash, (int)pose);
        }
    }
}