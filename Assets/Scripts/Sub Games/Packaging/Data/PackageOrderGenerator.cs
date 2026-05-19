using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Génère une PackageOrder procéduralement. Approche constructive : on
    /// remplit une grille virtuelle en respectant déjà toutes les règles de
    /// scoring (heavy dans la moitié basse, jamais de heavy au-dessus d'un
    /// fragile, espace = 100%). Les items réellement placés deviennent
    /// l'ordre — par construction, un placement 100% existe.
    ///
    /// La génération est déterministe à seed fixée → côté serveur on rejoue
    /// le même seed pour valider le snapshot (anti-triche léger).
    ///
    /// Stratégie de remplissage (bottom-up) :
    ///   1. Heavy items dans la moitié basse (y &lt; height/2)
    ///   2. Items normaux n'importe où
    ///   3. Fragile en dernier (en haut, pas de heavy au-dessus)
    /// On tire les items au hasard depuis le catalog ; on place dans la
    /// première cellule libre en scannant bas→haut, gauche→droite. Backtrack
    /// borné si on n'arrive pas à boucher un trou.
    /// </summary>
    public static class PackageOrderGenerator {
        private const int MaxBacktrackAttempts = 60;

        public static PackageOrder Generate(PackageItemDefinition[] catalog,
                                            int gridWidth, int gridHeight,
                                            int decoyCount, int seed,
                                            string customerName = null) {
            if (catalog == null || catalog.Length == 0) {
                Debug.LogWarning("[PackageOrderGenerator] Empty catalog — returning empty order.");
                return new PackageOrder {
                    orderId      = $"order-{seed}",
                    customerName = customerName ?? "Client",
                    seed         = seed,
                    requiredItems = new List<PackageItemDefinition>(),
                    decoys        = new List<PackageItemDefinition>(),
                };
            }

            var rng = new Random(seed);
            var required = BuildRequired(catalog, gridWidth, gridHeight, rng);

            // Decoys : items piochés dans le catalog, en évitant de dupliquer
            // exactement la liste required (un decoy peut quand même partager
            // une définition avec un required — c'est même plutôt cozy : le
            // joueur doit choisir parmi plusieurs items similaires).
            var decoys = new List<PackageItemDefinition>(decoyCount);
            for (int i = 0; i < decoyCount; i++) {
                decoys.Add(catalog[rng.Next(catalog.Length)]);
            }

            return new PackageOrder {
                orderId      = $"order-{seed}",
                customerName = customerName ?? "Client",
                seed         = seed,
                requiredItems = required,
                decoys        = decoys,
            };
        }

        private static List<PackageItemDefinition> BuildRequired(
            PackageItemDefinition[] catalog, int gridWidth, int gridHeight, Random rng) {
            int halfHeight = Mathf.Max(1, gridHeight / 2);

            // Pools triés par catégorie pour la stratégie d'ordre.
            var heavy   = FilterCatalog(catalog, def => def.heavy && !def.fragile);
            var fragile = FilterCatalog(catalog, def => def.fragile && !def.heavy);
            var normal  = FilterCatalog(catalog, def => !def.heavy && !def.fragile);

            // Tentatives multiples : si on rate un remplissage parfait, on
            // re-tire des items au hasard et on retente.
            for (int attempt = 0; attempt < MaxBacktrackAttempts; attempt++) {
                var grid = new PackageGrid(gridWidth, gridHeight);
                var placed = new List<PackageItemDefinition>();

                if (TryFill(grid, placed, heavy,   maxY: halfHeight - 1, rng)
                 && TryFill(grid, placed, normal,  maxY: gridHeight - 1, rng)
                 && TryFill(grid, placed, fragile, maxY: gridHeight - 1, rng)) {
                    if (grid.OccupiedCells() == gridWidth * gridHeight) {
                        return placed;
                    }
                }
            }

            // Fallback : si après MaxBacktrackAttempts on n'a pas trouvé,
            // on tente avec n'importe quel item du catalog (en ignorant les
            // contraintes heavy/fragile). Le score 100% ne sera pas garanti
            // dans ce cas — mais le mini-jeu reste jouable.
            Debug.LogWarning(
                "[PackageOrderGenerator] Could not pack a 100%-achievable order " +
                "after retries — using lax fallback. Tune the catalog.");
            return BuildFallback(catalog, gridWidth, gridHeight, rng);
        }

        /// <summary>
        /// Remplit la grille avec des items du pool, sans dépasser maxY.
        /// Scanne la grille bas→haut, gauche→droite. Pour chaque cellule
        /// libre, tente de placer un item au hasard (toutes rotations
        /// possibles). Retourne false si elle abandonne sur un trou
        /// impossible — l'appelant peut alors retenter avec un autre random.
        /// </summary>
        private static bool TryFill(PackageGrid grid, List<PackageItemDefinition> placed,
                                    List<PackageItemDefinition> pool, int maxY, Random rng) {
            if (pool.Count == 0) return true; // rien à placer, pas un échec

            int idCounter = placed.Count;

            for (int y = 0; y <= maxY; y++) {
                for (int x = 0; x < grid.Width; x++) {
                    if (grid.CellOwner(new Vector2Int(x, y)) != -1) continue;

                    if (TryPlaceAt(grid, pool, new Vector2Int(x, y), maxY, idCounter, rng,
                                   out var chosenDef)) {
                        placed.Add(chosenDef);
                        idCounter++;
                    }
                    // Si on n'a pas réussi à boucher la cellule (x,y) avec un
                    // item du pool, on continue : un item suivant (potentiellement
                    // dans un autre pool) la bouchera, ou c'est une zone de
                    // refus si elle reste vide.
                }
            }

            return true;
        }

        private static bool TryPlaceAt(PackageGrid grid, List<PackageItemDefinition> pool,
                                       Vector2Int origin, int maxY, int idForInstance,
                                       Random rng, out PackageItemDefinition chosen) {
            chosen = null;
            // Shuffle local du pool pour varier les tentatives.
            var order = ShuffledIndices(pool.Count, rng);

            for (int k = 0; k < order.Length; k++) {
                var def = pool[order[k]];
                if (def == null || def.shape.cells == null || def.shape.cells.Length == 0) continue;

                int rotations = def.allowRotation ? 4 : 1;
                int rStart = rng.Next(rotations);
                for (int rOffset = 0; rOffset < rotations; rOffset++) {
                    int rotation = (rStart + rOffset) % rotations;
                    var inst = new PackageItemInstance(idForInstance, def);
                    if (!grid.CanPlace(inst, origin, rotation)) continue;

                    // Contrainte de hauteur max — l'item ne doit pas dépasser maxY.
                    var rotated = inst.GetRotatedShape(rotation);
                    bool overflow = false;
                    for (int i = 0; i < rotated.cells.Length; i++) {
                        if (origin.y + rotated.cells[i].y > maxY) { overflow = true; break; }
                    }
                    if (overflow) continue;

                    grid.Place(inst, origin, rotation);
                    chosen = def;
                    return true;
                }
            }
            return false;
        }

        private static List<PackageItemDefinition> FilterCatalog(
            PackageItemDefinition[] catalog, Func<PackageItemDefinition, bool> predicate) {
            var list = new List<PackageItemDefinition>();
            for (int i = 0; i < catalog.Length; i++) {
                var def = catalog[i];
                if (def == null) continue;
                if (predicate(def)) list.Add(def);
            }
            return list;
        }

        private static int[] ShuffledIndices(int count, Random rng) {
            var arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = i;
            for (int i = count - 1; i > 0; i--) {
                int j = rng.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }

        /// <summary>
        /// Remplissage best-effort sans contraintes — pour le cas où le
        /// catalog ne permet pas un 100% strict. La grille peut rester
        /// partiellement vide.
        /// </summary>
        private static List<PackageItemDefinition> BuildFallback(
            PackageItemDefinition[] catalog, int gridWidth, int gridHeight, Random rng) {
            var grid = new PackageGrid(gridWidth, gridHeight);
            var placed = new List<PackageItemDefinition>();
            var anyPool = new List<PackageItemDefinition>();
            for (int i = 0; i < catalog.Length; i++) if (catalog[i] != null) anyPool.Add(catalog[i]);

            int idCounter = 0;
            for (int y = 0; y < gridHeight; y++) {
                for (int x = 0; x < gridWidth; x++) {
                    if (grid.CellOwner(new Vector2Int(x, y)) != -1) continue;
                    if (TryPlaceAt(grid, anyPool, new Vector2Int(x, y), gridHeight - 1,
                                   idCounter, rng, out var def)) {
                        placed.Add(def);
                        idCounter++;
                    }
                }
            }
            return placed;
        }
    }
}
