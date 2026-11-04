using System;
using Sim;
using UnityEngine;

namespace AI.States {
    /// <summary>
    /// État « occupé » générique : le joueur est engagé dans une action qui ouvre une UI
    /// bloquante (distributeur ATM, fouille d'un cadavre / sac de butin…). Tant qu'il y est,
    /// <see cref="PlayerState.BUSY"/> est posé — les systèmes d'input (hotbar, tir, mêlée)
    /// s'en servent pour se neutraliser le temps de l'action.
    ///
    /// La SORTIE est pilotée par un prédicat <c>stillBusy</c> : dès qu'il devient faux (l'UI
    /// s'est refermée par Échap, bouton, éloignement, inventaire fermé…), l'état se termine
    /// tout seul (retour Idle) au Tick suivant. Aucun appel explicite de fin n'est donc requis
    /// côté UI, ce qui couvre tous les chemins de fermeture.
    ///
    /// Pose faite à chaque entrée (pas dans le constructeur, qui ne tourne qu'à l'init de la
    /// state machine) via <see cref="PlayerController.BeginBusy"/>.
    /// </summary>
    public class CharacterBusy : IState {
        private readonly PlayerController player;

        private Func<bool> stillBusy;
        private Transform lookAt;
        private bool cancelOnMove;
        private Action onCancelled;

        public CharacterBusy(PlayerController player) {
            this.player = player;
        }

        /// <summary>Arme l'état avant la transition : prédicat de maintien + cible optionnelle
        /// vers laquelle orienter le joueur (ex. l'ATM, le cadavre).
        /// <paramref name="cancelOnMove"/> : une touche de DÉPLACEMENT annule l'action —
        /// <paramref name="onCancelled"/> referme alors l'UI déclencheuse (ex. grille de
        /// fouille) avant le retour à Idle. Par défaut (faux), le mouvement reste ignoré
        /// tant que l'UI est ouverte (comportement ATM historique).</summary>
        public void Configure(Func<bool> stillBusy, Transform lookAt,
                              bool cancelOnMove = false, Action onCancelled = null) {
            this.stillBusy = stillBusy;
            this.lookAt = lookAt;
            this.cancelOnMove = cancelOnMove;
            this.onCancelled = onCancelled;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.BUSY;
            if (this.lookAt != null) this.player.LookAt(this.lookAt);
        }

        public void Tick() {
            // Annulation par le MOUVEMENT (fouille…) : on referme l'UI déclencheuse et on
            // rend la main — le joueur « s'arrache » de l'action en bougeant.
            if (this.cancelOnMove && this.player.ReadMoveAxis().sqrMagnitude > 0.01f) {
                this.onCancelled?.Invoke();
                this.player.Idle();
                return;
            }

            // L'UI déclencheuse s'est refermée (ou n'a jamais pris) → on quitte l'état occupé.
            if (this.stillBusy == null || !this.stillBusy()) {
                this.player.Idle();
            }
        }

        public void OnExit() {
            // Sortie d'état par UN AUTRE chemin que le prédicat (un état tiers prend la main :
            // knockdown, assise, conduite…) : l'UI déclencheuse est peut-être encore ouverte —
            // on la referme via onCancelled. No-op sur les chemins normaux : prédicat déjà
            // faux (UI refermée d'elle-même) ou annulation par mouvement (onCancelled déjà
            // invoqué dans Tick, et la fermeture synchrone a rendu le prédicat faux).
            if (this.stillBusy != null && this.stillBusy()) this.onCancelled?.Invoke();
            this.stillBusy = null;
            this.lookAt = null;
            this.cancelOnMove = false;
            this.onCancelled = null;
        }
    }
}
