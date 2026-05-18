using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Bac de tri posé dans la scène. Chaque bac accepte une seule catégorie
    /// d'item. SortItemsStepInstance résout le tri en cherchant le bac le plus
    /// proche de la position de drop via FindClosest.
    /// </summary>
    public class SortingBin : MonoBehaviour {
        [Tooltip("Identifiant stable du bac. Doit être unique dans la scène.")]
        [SerializeField] private string binId;

        [Tooltip("Catégorie d'item acceptée par ce bac.")]
        [SerializeField] private SortingCategory acceptedCategory;

        private static readonly Dictionary<string, SortingBin> _all =
            new Dictionary<string, SortingBin>();

        public static IEnumerable<SortingBin> All => _all.Values;

        public string BinId => binId;
        public SortingCategory AcceptedCategory => acceptedCategory;

        private void Awake() {
            if (string.IsNullOrEmpty(binId)) {
                Debug.LogError($"[SortingBin] '{name}' has empty binId — fix it in the Inspector.");
                return;
            }
            if (_all.ContainsKey(binId))
                Debug.LogWarning($"[SortingBin] duplicate binId '{binId}' on '{name}'.");
            _all[binId] = this;
        }

        private void OnDestroy() {
            if (!string.IsNullOrEmpty(binId)) _all.Remove(binId);
        }

        /// <summary>
        /// Retourne le bac le plus proche de <paramref name="pos"/> dans le
        /// rayon <paramref name="maxRadius"/>, ou null si aucun.
        /// </summary>
        public static SortingBin FindClosest(Vector3 pos, float maxRadius) {
            float bestSqr = maxRadius * maxRadius;
            SortingBin best = null;
            foreach (var bin in _all.Values) {
                if (bin == null) continue;
                float sqr = (bin.transform.position - pos).sqrMagnitude;
                if (sqr < bestSqr) {
                    bestSqr = sqr;
                    best = bin;
                }
            }
            return best;
        }
    }
}
