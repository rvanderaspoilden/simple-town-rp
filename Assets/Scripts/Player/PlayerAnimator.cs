using System.Collections.Generic;
using UnityEngine;

namespace Sim {
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour {
        [Header("Held item visibility")]
        [Tooltip("Actions pendant lesquelles l'item tenu reste VISIBLE. NONE est toujours visible. " +
                 "Toute action absente de cette liste cache le renderer de l'item tenu (ex: SIT, SLEEP).")]
        [SerializeField] private List<CharacterAnimatorAction> actionsKeepingHeldItemsVisible = new();

        [Header("Starter Assets footsteps")]
        [Tooltip("Sons de pas joués par les AnimationEvents OnFootstep embarqués dans les clips " +
                 "de locomotion Starter Assets (un au hasard). Vide = silencieux mais sans erreur.")]
        [SerializeField] private AudioClip[] footstepAudioClips;
        [Tooltip("Son joué par l'AnimationEvent OnLand des clips d'atterrissage Starter Assets.")]
        [SerializeField] private AudioClip landingAudioClip;
        [Range(0f, 1f)]
        [SerializeField] private float footstepAudioVolume = 0.5f;

        private Animator animator;
        private PlayerHands playerHands;
        private CharacterAnimatorAction currentAction = CharacterAnimatorAction.NONE;

        private int velocityHash;
        private int groundedHash;
        private int jumpHash;
        private int freeFallHash;
        private int crouchHash;
        private int moodHash;
        private int actionHash;
        private int rightHandPoseHash;
        private int leftHandPoseHash;
        private int twoHandPoseHash;
        private int drinkingHandHash;

        private void Awake() {
            this.animator = GetComponent<Animator>();
            this.playerHands = GetComponent<PlayerHands>();
            this.velocityHash = Animator.StringToHash("Velocity");
            this.groundedHash = Animator.StringToHash("Grounded");
            this.jumpHash = Animator.StringToHash("Jump");
            this.freeFallHash = Animator.StringToHash("FreeFall");
            this.crouchHash = Animator.StringToHash("Crouch");
            this.moodHash = Animator.StringToHash("MoodType");
            this.actionHash = Animator.StringToHash("Action");
            this.rightHandPoseHash = Animator.StringToHash("RightHandPose");
            this.leftHandPoseHash = Animator.StringToHash("LeftHandPose");
            this.twoHandPoseHash = Animator.StringToHash("TwoHandPose");
            this.drinkingHandHash = Animator.StringToHash("DrinkingHand");
        }

        public void SetVelocity(float value) {
            this.animator.SetFloat(velocityHash, value);
        }

        // Saut TPS (états Jump/InAir du Base Layer, pattern Starter Assets).
        public void SetGrounded(bool value) {
            this.animator.SetBool(groundedHash, value);
        }

        public void SetJump(bool value) {
            this.animator.SetBool(jumpHash, value);
        }

        public void SetFreeFall(bool value) {
            this.animator.SetBool(freeFallHash, value);
        }

        /// <summary>Accroupi (état Crouch du Base Layer, locomotion Goblin Synty).</summary>
        public void SetCrouch(bool value) {
            this.animator.SetBool(crouchHash, value);
        }

        public void SetMood(float value) {
            this.animator.SetFloat(moodHash, value);
        }

        // ── AnimationEvents des clips Starter Assets ──────────────────────────────
        // Les clips de locomotion/atterrissage du pack embarquent OnFootstep/OnLand :
        // sans ces récepteurs, Unity log "AnimationEvent has no receiver". Même logique
        // que leur ThirdPersonController : on ne joue le son que si le clip émetteur pèse
        // assez dans le blend (évite les doubles pas pendant les transitions). Tourne sur
        // toutes les copies (local + distant), les pas des autres Broz sont donc audibles.

        private void OnFootstep(AnimationEvent animationEvent) {
            if (animationEvent.animatorClipInfo.weight <= 0.5f) return;
            if (this.footstepAudioClips == null || this.footstepAudioClips.Length == 0) return;
            int index = Random.Range(0, this.footstepAudioClips.Length);
            AudioSource.PlayClipAtPoint(this.footstepAudioClips[index], this.GetFootstepAudioPoint(), this.footstepAudioVolume);
        }

        private void OnLand(AnimationEvent animationEvent) {
            if (animationEvent.animatorClipInfo.weight <= 0.5f) return;
            if (this.landingAudioClip == null) return;
            AudioSource.PlayClipAtPoint(this.landingAudioClip, this.GetFootstepAudioPoint(), this.footstepAudioVolume);
        }

        // Centre de la capsule (y 0.9), équivalent du _controller.center du pack.
        private Vector3 GetFootstepAudioPoint() {
            return this.transform.TransformPoint(new Vector3(0f, 0.9f, 0f));
        }

        public void SetAction(CharacterAnimatorAction action) {
            this.currentAction = action;
            this.animator.SetFloat(actionHash, (float)action);
            this.ApplyHeldItemVisibility();
        }

        /// <summary>
        /// Drives the `DrinkingHand` int that overlays the drink gesture on a SINGLE arm:
        /// 0 = none, 1 = right, 2 = left. Only the targeted arm plays Drink, so the other
        /// hand keeps its current animation. Upper-body only — does not affect locomotion
        /// or held-item visibility.
        /// </summary>
        public void SetDrinkingHand(int hand) {
            this.animator.SetInteger(drinkingHandHash, hand);
        }

        /// <summary>
        /// Synchronise le rendu ET les poses de mains des items tenus avec l'action courante.
        /// Appelée à chaque changement d'action (SetAction) et de mains (PlayerHands.NotifyChanged)
        /// pour qu'un item ramassé pendant une action adopte aussitôt le bon état. Tourne sur
        /// toutes les copies (local + distant).
        ///
        /// Quand l'item est caché par une action (ex: SIT), on force aussi les poses de mains à
        /// NONE : sinon les layers Right/Left/Two Hand garderaient les bras fermés sur un objet
        /// invisible. Source unique de vérité — les poses et le renderer restent toujours cohérents.
        /// </summary>
        public void ApplyHeldItemVisibility() {
            if (this.playerHands == null) return;

            ItemBehaviour left  = this.playerHands.LeftHandItem;
            ItemBehaviour right = this.playerHands.RightHandItem;

            bool keepVisible = this.currentAction == CharacterAnimatorAction.NONE
                               || this.actionsKeepingHeldItemsVisible.Contains(this.currentAction);

            left?.SetRenderersVisible(keepVisible);
            right?.SetRenderersVisible(keepVisible);

            if (keepVisible) {
                ResolvedPose pose = HandPoseResolver.Resolve(left, right);
                this.SetRightHandPose(pose.Right);
                this.SetLeftHandPose(pose.Left);
                this.SetTwoHandPose(pose.TwoHand);
            } else {
                this.SetRightHandPose(HandPose.NONE);
                this.SetLeftHandPose(HandPose.NONE);
                this.SetTwoHandPose(TwoHandPose.NONE);
            }
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