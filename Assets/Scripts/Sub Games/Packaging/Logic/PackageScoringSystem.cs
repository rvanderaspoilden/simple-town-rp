using System.Collections.Generic;
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
        public readonly int placedCount;
        public readonly int totalCount;
        public readonly int decoyCount;

        public PackageScore(int total, PackageRating rating, float spaceRatio,
                            bool fragileOk, bool heavyOk, bool allItemsPlaced,
                            int placedCount = 0, int totalCount = 0, int decoyCount = 0) {
            this.total = total;
            this.rating = rating;
            this.spaceRatio = spaceRatio;
            this.fragileOk = fragileOk;
            this.heavyOk = heavyOk;
            this.allItemsPlaced = allItemsPlaced;
            this.placedCount = placedCount;
            this.totalCount = totalCount;
            this.decoyCount = decoyCount;
        }
    }

    /// <summary>
    /// Scoring cozy : jamais punitif. Le minimum est toujours "Correct".
    /// Pure logique (Vector2Int seulement comme dépendance Unity) → tourne
    /// tel quel côté serveur Mirror pour la validation anti-triche.
    ///
    /// Matching par définition (pas par instance) : si la commande demande
    /// 1 Sandwich, n'importe quelle instance de Sandwich placée compte —
    /// l'instance flag "decoy" du tray n'a aucune influence. Les placements
    /// qui dépassent la quantité requise (ou dont la définition n'est pas
    /// dans la commande) sont des "extras" et déclenchent la pénalité.
    /// </summary>
    public static class PackageScoringSystem {
        public static PackageScore Evaluate(PackageGrid grid, PackagingSubGameConfig cfg,
                                            IReadOnlyList<PackageItemDefinition> requiredItems) {
            int gridSize = grid.Width * grid.Height;

            // Build "quantité demandée par définition".
            var requiredByDef = new Dictionary<PackageItemDefinition, int>();
            int requiredTotal = 0;
            if (requiredItems != null) {
                for (int i = 0; i < requiredItems.Count; i++) {
                    var def = requiredItems[i];
                    if (def == null) continue;
                    requiredByDef.TryGetValue(def, out int c);
                    requiredByDef[def] = c + 1;
                    requiredTotal++;
                }
            }

            int requiredPlaced = 0;
            int extraItems = 0;
            int extraCells = 0;
            int requiredCells = 0;

            bool fragileOk = true;
            bool heavyOk = true;

            var placedSoFar = new Dictionary<PackageItemDefinition, int>();

            foreach (var kvp in grid.Items) {
                var item = kvp.Value;
                var def = item.Definition;
                int cells = CellCount(item);

                int already = placedSoFar.TryGetValue(def, out var p) ? p : 0;
                int needed  = requiredByDef.TryGetValue(def, out var r) ? r : 0;
                if (already < needed) {
                    requiredPlaced++;
                    requiredCells += cells;
                } else {
                    extraItems++;
                    extraCells += cells;
                }
                placedSoFar[def] = already + 1;

                // Physics rules apply to ALL placed items, extras included.
                if (def.heavy && item.Origin.y > grid.Height / 2) heavyOk = false;
                if (def.fragile && HasHeavyAbove(grid, item)) fragileOk = false;
            }

            // L'espace utile = cellules occupées par les items utiles à la
            // commande. Les extras ne comptent pas (ils ne remplissent pas le
            // colis pour rien).
            float spaceRatio = gridSize > 0 ? requiredCells / (float)gridSize : 0f;
            bool allPlaced = requiredPlaced >= requiredTotal;

            float spaceWeight   = cfg != null ? cfg.spaceWeight   : 0.5f;
            float fragileWeight = cfg != null ? cfg.fragileWeight : 0.25f;
            float heavyWeight   = cfg != null ? cfg.heavyWeight   : 0.25f;

            float normalized = spaceWeight + fragileWeight + heavyWeight;
            if (normalized <= 0f) normalized = 1f;

            float score =
                  (spaceWeight   * spaceRatio
                +  fragileWeight * (fragileOk ? 1f : 0.5f)
                +  heavyWeight   * (heavyOk   ? 1f : 0.5f)) / normalized;

            // Un colis incomplet ne peut pas atteindre Perfect : facteur 0.7.
            // Reste cozy (jamais < Correct grâce aux 0.5 de fragile/heavy).
            if (!allPlaced) score *= 0.7f;

            // Malus extras : -X% par cellule excédentaire. Score reste >= 0.
            float decoyPenalty = cfg != null ? cfg.decoyPenaltyPerCell : 0.05f;
            if (extraCells > 0 && decoyPenalty > 0f) {
                score = Mathf.Max(0f, score - extraCells * decoyPenalty);
            }

            int total = Mathf.RoundToInt(score * 1000f);

            PackageRating rating;
            if (total >= 900) rating = PackageRating.Perfect;
            else if (total >= 700) rating = PackageRating.Good;
            else rating = PackageRating.Correct;

            return new PackageScore(total, rating, spaceRatio, fragileOk, heavyOk, allPlaced,
                                    requiredPlaced, requiredTotal, extraItems);
        }

        /// <summary>
        /// Calcule le score à partir d'un snapshot réseau. Utilisé côté serveur
        /// avec l'ordre régénéré depuis la même seed (anti-triche léger).
        /// L'index dans le snapshot pointe dans une liste concaténée
        /// required + decoys — on déréférence en PackageItemDefinition puis on
        /// passe à Evaluate qui matche par définition.
        /// </summary>
        public static PackageScore EvaluateFromSnapshot(PackagePlacementSnapshot snapshot,
                                                       PackageOrder order,
                                                       PackagingSubGameConfig cfg) {
            int requiredCount = order != null && order.requiredItems != null
                ? order.requiredItems.Count : 0;
            int decoyCount = order != null && order.decoys != null
                ? order.decoys.Count : 0;
            if (requiredCount == 0) {
                return new PackageScore(0, PackageRating.Correct, 0f, true, true, false);
            }

            int width  = cfg != null ? cfg.gridWidth  : snapshot.gridWidth;
            int height = cfg != null ? cfg.gridHeight : snapshot.gridHeight;
            var grid = new PackageGrid(width, height);

            // Flat lookup : required puis decoys, dans l'ordre où le client les
            // a buildés. L'instanceIndex du placement adresse cette liste.
            var allDefs = new List<PackageItemDefinition>(requiredCount + decoyCount);
            allDefs.AddRange(order.requiredItems);
            allDefs.AddRange(order.decoys);

            if (snapshot.placements != null) {
                for (int i = 0; i < snapshot.placements.Length; i++) {
                    var p = snapshot.placements[i];
                    int idx = p.instanceIndex;
                    if (idx < 0 || idx >= allDefs.Count) continue;
                    var def = allDefs[idx];
                    if (def == null) continue;
                    var instance = new PackageItemInstance(idx, def);
                    // Place ignore les placements invalides (hors grille /
                    // chevauchement) — garde anti-triche supplémentaire.
                    grid.Place(instance, new Vector2Int(p.originX, p.originY), p.rotation);
                }
            }

            return Evaluate(grid, cfg, order.requiredItems);
        }

        private static int CellCount(PackageItemInstance item) {
            var shape = item.GetRotatedShape(item.Rotation);
            return shape.cells != null ? shape.cells.Length : 0;
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
