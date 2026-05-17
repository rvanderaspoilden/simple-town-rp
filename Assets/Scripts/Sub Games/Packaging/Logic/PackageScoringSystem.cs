using UnityEngine;

namespace Sim.SubGames.Packaging {
    public enum PackageRating {
        Correct,
        Good,
        Perfect
    }

    public readonly struct PackageScore {
        public readonly int total;
        public readonly PackageRating rating;
        public readonly float spaceRatio;
        public readonly bool fragileOk;
        public readonly bool heavyOk;
        public readonly bool allItemsPlaced;

        public PackageScore(int total, PackageRating rating, float spaceRatio,
                            bool fragileOk, bool heavyOk, bool allItemsPlaced) {
            this.total = total;
            this.rating = rating;
            this.spaceRatio = spaceRatio;
            this.fragileOk = fragileOk;
            this.heavyOk = heavyOk;
            this.allItemsPlaced = allItemsPlaced;
        }
    }

    /// <summary>
    /// Scoring cozy : jamais punitif. Le minimum est toujours "Correct".
    /// Pure logique (Vector2Int seulement comme dépendance Unity) → tourne
    /// tel quel côté serveur Mirror pour la validation anti-triche.
    /// </summary>
    public static class PackageScoringSystem {
        public static PackageScore Evaluate(PackageGrid grid, PackagingSubGameConfig cfg, int itemsInOrder) {
            float spaceRatio = grid.OccupiedCells() / (float)(grid.Width * grid.Height);
            bool allPlaced = grid.Items.Count == itemsInOrder;

            bool fragileOk = true;
            bool heavyOk = true;
            foreach (var kvp in grid.Items) {
                var item = kvp.Value;
                if (item.Definition.heavy && item.Origin.y > grid.Height / 2) heavyOk = false;
                if (item.Definition.fragile && HasHeavyAbove(grid, item)) fragileOk = false;
            }

            float spaceWeight   = cfg != null ? cfg.spaceWeight   : 0.5f;
            float fragileWeight = cfg != null ? cfg.fragileWeight : 0.25f;
            float heavyWeight   = cfg != null ? cfg.heavyWeight   : 0.25f;

            float normalized = spaceWeight + fragileWeight + heavyWeight;
            if (normalized <= 0f) normalized = 1f;

            float score =
                  (spaceWeight   * spaceRatio
                +  fragileWeight * (fragileOk ? 1f : 0.5f)
                +  heavyWeight   * (heavyOk   ? 1f : 0.5f)) / normalized;

            // Un colis incomplet ne peut pas atteindre Perfect : facteur 0.7
            // sur le score total. Reste cozy (jamais < Correct grâce aux 0.5
            // de fragile/heavy).
            if (!allPlaced) score *= 0.7f;

            int total = Mathf.RoundToInt(score * 1000f);

            PackageRating rating;
            if (total >= 900) rating = PackageRating.Perfect;
            else if (total >= 700) rating = PackageRating.Good;
            else rating = PackageRating.Correct;

            return new PackageScore(total, rating, spaceRatio, fragileOk, heavyOk, allPlaced);
        }

        /// <summary>
        /// Calcule le score à partir d'un snapshot réseau. Utilisé côté serveur
        /// avec la PackageOrderDefinition autoritaire (jamais celle envoyée par
        /// le client). Si la commande est introuvable, renvoie un score nul.
        /// </summary>
        public static PackageScore EvaluateFromSnapshot(PackagePlacementSnapshot snapshot,
                                                       PackageOrderDefinition order,
                                                       PackagingSubGameConfig cfg) {
            if (order == null || order.items == null || order.items.Length == 0) {
                return new PackageScore(0, PackageRating.Correct, 0f, true, true, false);
            }

            int width  = cfg != null ? cfg.gridWidth  : snapshot.gridWidth;
            int height = cfg != null ? cfg.gridHeight : snapshot.gridHeight;
            var grid = new PackageGrid(width, height);

            if (snapshot.placements != null) {
                for (int i = 0; i < snapshot.placements.Length; i++) {
                    var p = snapshot.placements[i];
                    if (p.instanceIndex >= order.items.Length) continue;
                    var def = order.items[p.instanceIndex];
                    if (def == null) continue;
                    var instance = new PackageItemInstance(p.instanceIndex, def);
                    // Place ignore les placements invalides (hors grille / chevauchement)
                    // — autre garde anti-triche.
                    grid.Place(instance, new Vector2Int(p.originX, p.originY), p.rotation);
                }
            }

            return Evaluate(grid, cfg, order.items.Length);
        }

        private static bool HasHeavyAbove(PackageGrid grid, PackageItemInstance item) {
            var shape = item.GetRotatedShape(item.Rotation);
            for (int i = 0; i < shape.cells.Length; i++) {
                var above = item.Origin + shape.cells[i] + Vector2Int.up;
                if (!grid.IsInside(above)) continue;
                int owner = grid.CellOwner(above);
                if (owner == -1 || owner == item.Id) continue;
                if (grid.Items[owner].Definition.heavy) return true;
            }
            return false;
        }
    }
}
