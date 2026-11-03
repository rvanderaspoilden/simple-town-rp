using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// Animations d'attaque PROCÉDURALES de la vue client d'un NPC hostile — reprise directe
    /// des poses additives de PlayerMelee (swing d'arme, coups de poing alternés) + une pose
    /// de visée « bras tendu » pour les armes à feu, avec un petit kick de recul au tir.
    /// Rotations additives des os appliquées en LateUpdate (après l'Animator), aucun clip
    /// requis, compose avec n'importe quelle pose du blend tree. Ajouté à la volée par
    /// ClientNpcView quand la config du NPC est hostile. Purement visuel, aucun réseau.
    /// </summary>
    public class NpcAttackAnimator : MonoBehaviour {
        // Timeline du swing d'arme (s) — identique à PlayerMelee.
        private const float WindupEnd     = 0.2f;
        private const float StrikeEnd     = 0.36f;
        private const float SwingDuration = 0.6f;

        // Timeline du coup de poing (s).
        private const float PunchWindupEnd = 0.1f;
        private const float PunchStrikeEnd = 0.2f;
        private const float PunchDuration  = 0.38f;

        // Angles du swing (mêmes valeurs par défaut que PlayerMelee).
        private const float RaiseAngle         = 140f;
        private const float FollowThroughAngle = 25f;
        private const float ElbowBendAngle     = 35f;
        private const float SpineTwistAngle    = 14f;

        // Angles du punch.
        private const float PunchRaiseAngle = 75f;
        private const float PunchElbowCock  = 70f;
        private const float PunchSpineTwist = 18f;

        // Pose de visée (arme à feu tenue par un hostile en état Attacking).
        private const float AimArmRaise     = 72f;
        private const float AimElbowBend    = 12f;
        private const float AimBlendSpeed   = 5f;
        private const float FireKickAngle   = 9f;
        private const float FireKickRecover = 0.18f;

        private Animator _animator;

        private float _swingStart = float.NegativeInfinity;
        private bool  _isPunch;
        private bool  _punchLeft;

        private bool  _aimHold;
        private float _aimBlend;
        private float _fireStart = float.NegativeInfinity;

        private void Awake() {
            _animator = GetComponent<Animator>();
        }

        /// <summary>Swing d'arme de mêlée (hache…), bras droit.</summary>
        public void PlaySwing() {
            _swingStart = Time.time;
            _isPunch = false;
        }

        /// <summary>Coup de poing (droit ou gauche).</summary>
        public void PlayPunch(bool leftFist) {
            _swingStart = Time.time;
            _isPunch = true;
            _punchLeft = leftFist;
        }

        /// <summary>Kick de recul au tir (la pose de visée est pilotée par SetAimHold).</summary>
        public void PlayFire() {
            _fireStart = Time.time;
        }

        /// <summary>Tient (ou relâche) la pose de visée « bras tendu » — activée par la vue
        /// quand le NPC est en état Attacking avec une arme à feu.</summary>
        public void SetAimHold(bool value) {
            _aimHold = value;
        }

        private void LateUpdate() {
            if (_animator == null || !_animator.isActiveAndEnabled) return;

            _aimBlend = Mathf.MoveTowards(_aimBlend, _aimHold ? 1f : 0f, Time.deltaTime * AimBlendSpeed);

            float t = Time.time - _swingStart;
            bool swinging = t >= 0f && t < (_isPunch ? PunchDuration : SwingDuration);
            if (swinging) {
                if (_isPunch) ApplyPunchPose(t);
                else ApplySwingPose(t);
                return;
            }

            if (_aimBlend > 0.01f) ApplyAimPose();
        }

        /// <summary>Pose du swing à l'instant t (cf. PlayerMelee.ApplySwingPose).</summary>
        private void ApplySwingPose(float t) {
            float lift;
            if (t < WindupEnd) {
                float p = t / WindupEnd;
                lift = RaiseAngle * (1f - (1f - p) * (1f - p));
            }
            else if (t < StrikeEnd) {
                float p = (t - WindupEnd) / (StrikeEnd - WindupEnd);
                lift = Mathf.Lerp(RaiseAngle, FollowThroughAngle, p * p);
            }
            else {
                float p = (t - StrikeEnd) / (SwingDuration - StrikeEnd);
                lift = Mathf.Lerp(FollowThroughAngle, 0f, p);
            }

            Transform upperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform lowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform chest    = _animator.GetBoneTransform(HumanBodyBones.Chest);

            float raiseRatio = Mathf.Clamp01(lift / RaiseAngle);
            if (upperArm != null) upperArm.Rotate(transform.right, -lift, Space.World);
            if (lowerArm != null) lowerArm.Rotate(transform.right, -ElbowBendAngle * raiseRatio, Space.World);
            if (chest    != null) chest.Rotate(Vector3.up, SpineTwistAngle * raiseRatio, Space.World);
        }

        /// <summary>Pose du coup de poing à l'instant t (cf. PlayerMelee.ApplyPunchPose).</summary>
        private void ApplyPunchPose(float t) {
            bool left = _punchLeft;

            float extend;
            float cock;
            if (t < PunchWindupEnd) {
                float p = t / PunchWindupEnd;
                extend = 0.55f * p;
                cock = p;
            }
            else if (t < PunchStrikeEnd) {
                float p = (t - PunchWindupEnd) / (PunchStrikeEnd - PunchWindupEnd);
                extend = Mathf.Lerp(0.55f, 1f, p * p);
                cock = 1f - p * p;
            }
            else {
                float p = (t - PunchStrikeEnd) / (PunchDuration - PunchStrikeEnd);
                extend = 1f - p;
                cock = 0f;
            }

            Transform upperArm = _animator.GetBoneTransform(
                left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lowerArm = _animator.GetBoneTransform(
                left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform chest = _animator.GetBoneTransform(HumanBodyBones.Chest);

            if (upperArm != null) upperArm.Rotate(transform.right, -PunchRaiseAngle * extend, Space.World);
            if (lowerArm != null) lowerArm.Rotate(transform.right, -PunchElbowCock * cock, Space.World);
            if (chest != null) {
                float twist = PunchSpineTwist * extend;
                chest.Rotate(Vector3.up, left ? twist : -twist, Space.World);
            }
        }

        /// <summary>Pose de visée : bras droit levé vers l'avant (le corps est déjà orienté
        /// vers la cible par le serveur), avec un kick bref du canon au tir.</summary>
        private void ApplyAimPose() {
            float kick = 0f;
            float sinceFire = Time.time - _fireStart;
            if (sinceFire >= 0f && sinceFire < FireKickRecover) {
                kick = FireKickAngle * (1f - sinceFire / FireKickRecover);
            }

            Transform upperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform lowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);

            if (upperArm != null) {
                upperArm.Rotate(transform.right, (-AimArmRaise - kick) * _aimBlend, Space.World);
            }
            if (lowerArm != null) {
                lowerArm.Rotate(transform.right, -AimElbowBend * _aimBlend, Space.World);
            }
        }
    }
}
