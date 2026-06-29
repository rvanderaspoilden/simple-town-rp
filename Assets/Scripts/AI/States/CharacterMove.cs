using Interaction;
using Sim;
using UnityEngine;

namespace AI.States {
    public class CharacterMove : IState {
        private readonly PlayerController player;

        // Throttle de re-pathing vers une cible mobile : 5 Hz suffit pour suivre un NPC qui marche
        // sans saturer le recompute de path (SetDestination invalide le path courant à chaque appel).
        private const float MobileRetargetInterval = 0.2f;
        private float _nextRetargetAt;

        public CharacterMove(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.MOVING;
            this._nextRetargetAt = 0f;
        }

        public void Tick() {
            // Tracking de cible mobile : si l'on marche vers un IInteractable (NPC, joueur, véhicule
            // mobile), on rafraîchit la destination vers sa position courante au lieu de garder le
            // point figé au clic. Throttle 5 Hz pour ne pas écraser le path à chaque frame.
            IInteractable target = this.player.InteractableTarget;
            if (target != null && (target as UnityEngine.Object) != null && Time.time >= this._nextRetargetAt) {
                this.player.NavMeshAgent.SetDestination(target.transform.position);
                this._nextRetargetAt = Time.time + MobileRetargetInterval;
            }

            MarkerController.Instance.ShowAt(this.player.NavMeshAgent.pathEndPosition);

            // desiredVelocity (target velocity along the path) instead of velocity (smoothed by
            // agent acceleration): the animator parameter snaps 0 ↔ speed so the blend tree picks
            // exactly one of idle/walk/run instead of mixing all three during accel/decel.
            Vector3 desired = this.player.NavMeshAgent.desiredVelocity;
            this.player.Animator.SetVelocity(desired.magnitude);

            // Flatten on the horizontal plane before LookRotation: desiredVelocity can carry a
            // vertical (Y) component near NavMesh borders/slopes, and feeding that to LookRotation
            // pitches the character forward/backward instead of only yawing it.
            Vector3 flatDesired = new Vector3(desired.x, 0f, desired.z);
            if (flatDesired.sqrMagnitude > 0.0001f) {
                this.player.transform.rotation = Quaternion.LookRotation(flatDesired.normalized);
            }
        }

        public void OnExit() {
            this.player.NavMeshAgent.ResetPath();

            MarkerController.Instance.Hide();
        }
    }
}