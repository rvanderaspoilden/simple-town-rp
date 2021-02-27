using UnityEngine;

namespace Sim {
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour {
        private Animator animator;

        private int velocityHash;
        private int moodHash;

        private void Awake() {
            this.animator = GetComponent<Animator>();
            this.velocityHash = Animator.StringToHash("Velocity");
            this.moodHash = Animator.StringToHash("Mood");
        }

        public void SetVelocity(float value) {
            this.animator.SetFloat(velocityHash, value);
        }

        public void SetMood(int value) {
            this.animator.SetFloat(moodHash, value);
        }
    }
}