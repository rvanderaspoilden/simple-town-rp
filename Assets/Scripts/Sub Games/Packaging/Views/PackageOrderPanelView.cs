using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Affiche la commande client : nom du client + liste d'items avec quantités.
    /// Met à jour les coches au fur et à mesure que les items sont posés/retirés
    /// de la grille (via UpdateProgress).
    /// </summary>
    public class PackageOrderPanelView : MonoBehaviour {
        [SerializeField] private TextMeshProUGUI customerLabel;
        [SerializeField] private RectTransform itemListContainer;
        [SerializeField] private PackageOrderItemRow rowPrefab;

        // Définitions distinctes -> total demandé
        private readonly Dictionary<PackageItemDefinition, int> _totals =
            new Dictionary<PackageItemDefinition, int>();
        // Définitions distinctes -> row UI
        private readonly Dictionary<PackageItemDefinition, PackageOrderItemRow> _rows =
            new Dictionary<PackageItemDefinition, PackageOrderItemRow>();

        public void Build(PackageOrder order) {
            // Reset
            foreach (var r in _rows.Values) if (r != null) Destroy(r.gameObject);
            _rows.Clear();
            _totals.Clear();

            if (order == null || order.requiredItems == null) return;

            if (customerLabel != null) customerLabel.text = order.customerName;

            // Aggrege les doublons (2 peluches -> "2x")
            for (int i = 0; i < order.requiredItems.Count; i++) {
                var def = order.requiredItems[i];
                if (def == null) continue;
                if (!_totals.ContainsKey(def)) _totals[def] = 0;
                _totals[def]++;
            }

            foreach (var kvp in _totals) {
                var row = Instantiate(rowPrefab, itemListContainer);
                row.Bind(kvp.Key, kvp.Value);
                _rows[kvp.Key] = row;
            }
        }

        /// <summary>
        /// Met à jour les coches selon les instances actuellement posées dans la grille.
        /// </summary>
        public void UpdateProgress(IReadOnlyDictionary<int, PackageItemInstance> placedItems) {
            // Comptage placé par définition — n'importe quelle instance d'une
            // définition demandée coche la ligne correspondante (cap à la
            // quantité requise). Le tray "decoy" n'a aucune influence : si le
            // joueur a deux Sandwich visuellement identiques et qu'il en pose
            // un, ça compte — peu importe quel slot il a pris.
            var counts = new Dictionary<PackageItemDefinition, int>();
            foreach (var kvp in placedItems) {
                var def = kvp.Value.Definition;
                if (!counts.ContainsKey(def)) counts[def] = 0;
                counts[def]++;
            }

            foreach (var kvp in _rows) {
                int total = _totals.TryGetValue(kvp.Key, out var t) ? t : 0;
                int placed = counts.TryGetValue(kvp.Key, out var c) ? Mathf.Min(c, total) : 0;
                kvp.Value.SetPlacedCount(placed, total);
            }
        }
    }
}
