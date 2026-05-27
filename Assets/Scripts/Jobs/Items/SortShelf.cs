using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Étagère de spawn pour la mission de tri. Pose ce composant sur le prop
    /// étagère et renseigne des Transforms enfants comme <see cref="slots"/>
    /// (un par emplacement de colis). Le SortItemsStep pose chaque colis sur le
    /// slot d'index correspondant — position ET rotation — dans l'ordre des
    /// SortTask.
    ///
    /// Registre statique par <see cref="shelfId"/>, même pattern que SortingBin
    /// et JobPoint. La scène (City.unity) étant chargée côté serveur, ce
    /// composant existe aussi serveur-side : c'est le serveur qui lit les slots
    /// au spawn des items.
    /// </summary>
    public class SortShelf : MonoBehaviour {
        [Tooltip("Identifiant stable de l'étagère. Doit être unique dans la scène.")]
        [SerializeField] private string shelfId;

        [Tooltip("Emplacements de spawn, dans l'ordre. Chaque colis est posé sur le " +
                 "slot d'index correspondant (position + rotation du Transform). " +
                 "Crée des GameObjects vides enfants de l'étagère et place-les ici.")]
        [SerializeField] private List<Transform> slots = new List<Transform>();

        private static readonly Dictionary<string, SortShelf> _all =
            new Dictionary<string, SortShelf>();

        public string ShelfId => shelfId;
        public int SlotCount => slots.Count;

        /// <summary>Slot d'index donné, ou null si hors limites / non assigné.</summary>
        public Transform GetSlot(int index)
            => index >= 0 && index < slots.Count ? slots[index] : null;

        private void Awake() {
            if (string.IsNullOrEmpty(shelfId)) {
                Debug.LogError($"[SortShelf] '{name}' has empty shelfId — fix it in the Inspector.");
                return;
            }
            if (_all.ContainsKey(shelfId))
                Debug.LogWarning($"[SortShelf] duplicate shelfId '{shelfId}' on '{name}'.");
            _all[shelfId] = this;
        }

        private void OnDestroy() {
            if (!string.IsNullOrEmpty(shelfId)
                && _all.TryGetValue(shelfId, out var s) && s == this)
                _all.Remove(shelfId);
        }

        public static SortShelf Get(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            _all.TryGetValue(id, out var shelf);
            return shelf;
        }

#if UNITY_EDITOR
        // Visualise les slots dans la scène : sphère = position, rayon = orientation (forward).
        private void OnDrawGizmos() {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < slots.Count; i++) {
                if (slots[i] == null) continue;
                Gizmos.DrawWireSphere(slots[i].position, 0.08f);
                Gizmos.DrawRay(slots[i].position, slots[i].forward * 0.2f);
            }
        }
#endif
    }
}
