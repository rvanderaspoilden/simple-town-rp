using UnityEngine;

namespace Sim {
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour {

        private Animator animator;

        private void Awake() {
            this.animator = GetComponent<Animator>();
        }

        public void SetVelocity(float value) {
            this.animator.SetFloat("Velocity", value);
        }
    }
}
