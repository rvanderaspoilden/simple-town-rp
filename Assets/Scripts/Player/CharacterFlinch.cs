using UnityEngine;

namespace Sim {
    /// <summary>
    /// Petite animation de choc procédurale quand un personnage encaisse un coup NON létal :
    /// le buste part brièvement en arrière avec une légère vrille, puis revient (~0.35 s).
    /// Rotation additive des os appliquée en LateUpdate (après l'Animator), même technique
    /// que le swing de PlayerMelee — aucun clip requis, compose avec n'importe quelle pose.
    ///
    /// Partagé joueurs/PNJ : ajouté à la volée (AddComponent) par PlayerHealth.RpcFlinch et
    /// ClientNpcView.PlayHitReact — purement visuel, aucun état réseau.
    /// </summary>
    public class CharacterFlinch : MonoBehaviour {
        private const float Duration = 0.35f;
        private const float PitchAngle = 14f; // recul du buste (deg) au pic du choc
        private const float TwistAngle = 8f;  // vrille latérale (deg), signe aléatoire par coup

        private Animator animator;
        private float startTime = float.NegativeInfinity;
        private float twistSign = 1f;

        private void Awake() {
            this.animator = GetComponent<Animator>();
        }

        /// <summary>Déclenche (ou relance) le choc. Sans effet si aucun Animator humanoïde.</summary>
        public void Play() {
            this.startTime = Time.time;
            this.twistSign = Random.value < 0.5f ? -1f : 1f;
        }

        private void LateUpdate() {
            float t = Time.time - this.startTime;
            if (t < 0f || t >= Duration || this.animator == null) return;

            // Pic immédiat puis retour amorti : impulsion = (1-p)² — sèche au début, douce à la fin.
            float p = t / Duration;
            float impulse = (1f - p) * (1f - p);

            Transform chest = this.animator.GetBoneTransform(HumanBodyBones.Chest);
            Transform head = this.animator.GetBoneTransform(HumanBodyBones.Head);
            if (chest != null) {
                chest.Rotate(this.transform.right, PitchAngle * impulse, Space.World);
                chest.Rotate(Vector3.up, TwistAngle * this.twistSign * impulse, Space.World);
            }
            // La tête accentue légèrement le coup (fouettée un peu plus que le buste).
            if (head != null) head.Rotate(this.transform.right, PitchAngle * 0.5f * impulse, Space.World);
        }

        /// <summary>Récupère (ou ajoute) le CharacterFlinch d'un personnage et joue le choc.</summary>
        public static void PlayOn(GameObject character) {
            if (character == null) return;
            CharacterFlinch flinch = character.GetComponent<CharacterFlinch>();
            if (flinch == null) flinch = character.AddComponent<CharacterFlinch>();
            flinch.Play();
        }
    }
}
