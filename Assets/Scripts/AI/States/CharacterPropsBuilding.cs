using System;
using Sim;
using UnityEngine;

namespace AI.States {
    /// <summary>
    /// Timed prop-construction state (déclenché par l'action BUILD sur un prop). Le
    /// personnage joue une animation en boucle pendant <c>duration</c> secondes ; le retour
    /// visuel est porté par le VFX de construction (révélation du mesh, networké). À la fin →
    /// <c>onComplete</c> (envoie la requête de construction) puis retour idle.
    ///
    /// IMPORTANT : ce state NE TOUCHE PAS au StateType du joueur (donc la caméra reste en
    /// mode FREE — pas de bascule en mode BUILD). L'annulation est automatique : tout autre
    /// state qui prend la main (le joueur se déplace, interagit…) remplace celui-ci →
    /// <see cref="OnExit"/> s'exécute sans complétion, stoppe l'anim. La requête de
    /// construction n'est donc jamais envoyée (le prop reste à construire).
    /// </summary>
    public class CharacterPropsBuilding : IState {
        // Looping "working" animation played while the build runs.
        // Maps to the Searching clip (Action blend-tree threshold 8).
        private const CharacterAnimatorAction BuildAnim = CharacterAnimatorAction.SEARCHING;

        private readonly PlayerController player;
        private readonly float duration;
        private readonly Action onComplete;
        private readonly Action onCancel;    // sortie sans complétion (déplacement / interaction)

        private float _elapsed;
        private bool _completed;

        public CharacterPropsBuilding(PlayerController player, float duration, Action onComplete, Action onCancel = null) {
            this.player = player;
            this.duration = Mathf.Max(0.01f, duration);
            this.onComplete = onComplete;
            this.onCancel = onCancel;
        }

        public void OnEnter() {
            _elapsed = 0f;
            _completed = false;
            this.player.SetAnimatorAction(BuildAnim);
            // Le retour de progression est porté par le VFX de construction (révélation du
            // mesh), piloté par le réseau dans PropBehaviourBase — rien à afficher ici.
        }

        public void Tick() {
            if (_completed) return;

            _elapsed += Time.deltaTime;

            if (_elapsed >= duration) {
                _completed = true;
                this.onComplete?.Invoke();   // envoie la requête de construction (+ finale VFX)
                this.player.Idle();          // → stateMachine swap idle → OnExit cleanup
            }
        }

        public void OnExit() {
            this.player.SetAnimatorAction(CharacterAnimatorAction.NONE);

            // Sortie sans complétion = annulation (déplacement / interaction) → stoppe le VFX
            // sur tous les clients. Aucun StateType modifié, la requête de build n'a pas été envoyée.
            if (!_completed) this.onCancel?.Invoke();
        }
    }
}
