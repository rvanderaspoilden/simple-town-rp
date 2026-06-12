using System;
using Sim.Player;
using UnityEngine;

namespace Sim.Constellation {
    /// <summary>
    /// Point unique de traduction « nœud débloqué → effet passif de gameplay ».
    /// Tout le savoir métier sur les effets des nœuds (ids + valeurs) vit ICI, pas
    /// éparpillé dans les systèmes consommateurs (reward kinds, mouvement…).
    ///
    /// Deux contextes d'appel :
    ///   - SERVEUR (récompenses) : passer <see cref="PlayerConstellation.ServerHasUnlockedNode"/>
    ///     (cache serveur hydraté au connect).
    ///   - CLIENT local (mouvement) : passer <c>id =&gt; provider.State.IsUnlocked(id)</c>.
    /// Le délégué <c>has</c> découple le helper de la source d'autorité.
    ///
    /// Ajouter un effet passif = ajouter une constante d'id + une ligne dans le calcul
    /// correspondant. Les nœuds purement « gate de mission » ne passent pas par ici
    /// (ils sont câblés via MissionDefinition.requiredNode).
    /// </summary>
    public static class ConstellationPerks {
        // ── Devises de branche (BranchConfig.id) ───────────────────────────
        // Branche "Ingenious" (bricolage/artisanat). La branche "Job" (ex-Ingenieux)
        // est le tronc métier (devise interne non utilisée).
        public const string IngeniousBranchId = "Ingenious";
        public const string SociableBranchId  = "Sociable";

        // ── Métier Livreur (sous-branche delivery_driver) ──────────────────
        public const string DeliveryProfessionId = "delivery_driver";

        public const string DeliverySpeedNode  = "delivery_driver_speed";   // +10 % vitesse
        public const string DeliveryTipsNode   = "delivery_driver_tips";    // +15 % gains
        public const string DeliveryMasterNode = "delivery_driver_master";  // +5 % gains
        public const string DeliveryRushNode   = "delivery_driver_rush";    // prime de rapidité

        // Bonus max de la Prime de rapidité, atteint sur une livraison parfaite (rapide).
        public const float RushMaxBonus = 0.30f;

        /// <summary>
        /// Multiplicateur appliqué aux gains argent d'une mission, selon les nœuds
        /// passifs débloqués. 1.0 = aucun bonus. Cumul additif des bonus actifs.
        /// </summary>
        public static float EarningsMultiplier(string professionId, Func<string, bool> has) {
            if (has == null) return 1f;
            float bonus = 0f;
            if (professionId == DeliveryProfessionId) {
                if (has(DeliveryTipsNode))   bonus += 0.15f; // pourboires
                if (has(DeliveryMasterNode)) bonus += 0.05f; // maîtrise
            }
            return 1f + bonus;
        }

        /// <summary>Surcharge serveur : lit le cache du PlayerConstellation.</summary>
        public static float EarningsMultiplier(PlayerConstellation pc, string professionId) =>
            pc == null ? 1f : EarningsMultiplier(professionId, pc.ServerHasUnlockedNode);

        /// <summary>
        /// Bonus ADDITIONNEL de la Prime de rapidité (nœud <see cref="DeliveryRushNode"/>),
        /// proportionnel à la vitesse de réalisation. <paramref name="speedFraction"/> ∈ [0,1]
        /// (1 = livraison parfaite/rapide, dérivé du rating temps). 0 si le nœud n'est pas
        /// débloqué ou hors métier livreur. À AJOUTER au multiplicateur de gains.
        /// </summary>
        public static float RushEarningsBonus(string professionId, float speedFraction, Func<string, bool> has) {
            if (has == null || professionId != DeliveryProfessionId) return 0f;
            if (!has(DeliveryRushNode)) return 0f;
            return RushMaxBonus * Mathf.Clamp01(speedFraction);
        }

        /// <summary>
        /// Multiplicateur de vitesse de déplacement issu des nœuds passifs. 1.0 = base.
        /// Appliqué côté client local (le mouvement est piloté par l'owner).
        /// </summary>
        public static float MoveSpeedMultiplier(Func<string, bool> has) {
            if (has == null) return 1f;
            float bonus = 0f;
            if (has(DeliverySpeedNode)) bonus += 0.10f;
            return 1f + bonus;
        }

        // ── Autres branches ────────────────────────────────────────────────
        public const string CreatifDecorationNode  = "creatif_deco";          // gate du mode peinture
        public const string SociableRencontresNode = "sociable_rencontres";   // bonus points sur don
        public const string IngeniousBricoleurNode = "ingenious_bricoleur";   // x2 points de bricolage
        public const string IngeniousArtisanNode   = "ingenious_artisan";     // bonus de rapidité (enfant de Bricoleur)

        // Bricoleur : poser (build) un prop crédite des points Ingenious. Boucle
        // d'économie de la branche Ingenious. Le nœud Bricoleur double le gain.
        public const int BuildBaseIngeniousPoints = 2;
        public static int BuildIngeniousPoints(Func<string, bool> has) {
            int pts = BuildBaseIngeniousPoints;
            if (has != null && has(IngeniousBricoleurNode)) pts *= 2;
            return pts;
        }

        // Artisan (nœud ENFANT de Bricoleur) : BONUS DE RAPIDITÉ — la construction est plus
        // rapide. Multiplicateur appliqué à PropsConfig.BuildDuration (côté client local).
        // Bricoleur doit être débloqué d'abord (Artisan est son enfant dans le graphe).
        public const float ArtisanBuildSpeedup = 0.5f; // ×0.5 = 2x plus rapide
        public static float BuildDurationMultiplier(Func<string, bool> has) =>
            (has != null && has(IngeniousArtisanNode)) ? ArtisanBuildSpeedup : 1f;

        // Rencontres : offrir de l'argent à un joueur crédite des points Sociable,
        // uniquement si le nœud est débloqué (0 sinon).
        public const int GiftSociablePoints = 1;
        public static int GiftSociablePointsFor(Func<string, bool> has) =>
            (has != null && has(SociableRencontresNode)) ? GiftSociablePoints : 0;

        // ── Branche Environnement ──────────────────────────────────────────
        public const string EnvironnementBranchId = "Environnement";
        public const string EnvironnementEcoloNode = "environnement_ecolo"; // débloque un cosmétique

        // Jeter un objet à la poubelle crédite des points Environnement.
        public const int TrashEnvironnementPoints = 1;

        /// <summary>True si le joueur a débloqué le cosmétique Écolo (nœud Ecolo).
        /// Gate à brancher où le cosmétique est appliqué/proposé.</summary>
        public static bool HasEcoloCosmetic(Func<string, bool> has) =>
            has != null && has(EnvironnementEcoloNode);
    }
}
