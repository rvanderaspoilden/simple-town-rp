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

            int total = Mathf.RoundToInt(score * 1000f);

            PackageRating rating;
            if (total >= 900) rating = PackageRating.Perfect;
            else if (total >= 700) rating = PackageRating.Good;
            else rating = PackageRating.Correct;

            return new PackageScore(total, rating, spaceRatio, fragileOk, heavyOk, allPlaced);
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
