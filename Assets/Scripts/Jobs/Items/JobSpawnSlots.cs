using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Fournisseur générique d'emplacements de spawn pour les steps Jobs.
    /// Utilisé aussi bien par le tri (SortItemsStep) que par la livraison
    /// (PickupPackageStep, ex. une palette de dépôt).
    ///
    /// Pose ce composant sur le prop (palette, étagère, table…) et renseigne des
    /// Transforms enfants comme <see cref="slots"/>. Le step lit le slot d'index
    /// voulu et y pose l'item (position ET rotation du Transform).
    ///
    /// **Table d'attribution** : un slot est marqué *occupé* dès qu'un item est
    /// spawné dessus (<see cref="TryReserve"/>) ; il redevient libre quand le step
    /// libère sa réservation (<see cref="Release"/> / <see cref="ReleaseByEntity"/>),
    /// typiquement à la sortie du step (ramassage ou nettoyage). Il n'y a AUCUNE
    /// détection physique : c'est purement une comptabilité serveur tenue par ce
    /// composant.
    ///
    /// Registre statique par <see cref="slotsId"/>, même pattern que
    /// SortingBin / JobPoint. La scène (City.unity) étant chargée côté serveur,
    /// ce composant existe aussi serveur-side : c'est le serveur qui lit les slots
    /// au spawn des items.
    /// </summary>
    public class JobSpawnSlots : MonoBehaviour {
        [Tooltip("Identifiant stable du groupe de slots. Doit être unique dans la scène.")]
        [SerializeField] private string slotsId;

        [Tooltip("Emplacements de spawn, dans l'ordre. Le step pose l'item sur le " +
                 "slot d'index demandé (position + rotation du Transform). " +
                 "Crée des GameObjects vides enfants du prop et place-les ici.")]
        [SerializeField] private List<Transform> slots = new List<Transform>();

        private static readonly Dictionary<string, JobSpawnSlots> _all =
            new Dictionary<string, JobSpawnSlots>();

        // slotIndex → entityId de l'item posé sur ce slot. Tant qu'une entrée existe,
        // le slot est considéré OCCUPÉ et ne peut pas accueillir un nouveau spawn.
        private readonly Dictionary<int, int> _occupied = new Dictionary<int, int>();

        public string SlotsId => slotsId;
        public int SlotCount => slots.Count;

        /// <summary>Slot d'index donné, ou null si hors limites / non assigné.</summary>
        public Transform GetSlot(int index)
            => index >= 0 && index < slots.Count ? slots[index] : null;

        /// <summary>Vrai si le slot existe (Transform non null) ET n'est pas réservé.</summary>
        public bool IsSlotFree(int index)
            => GetSlot(index) != null && !_occupied.ContainsKey(index);

        /// <summary>
        /// Liste (re-construite à chaque appel) des indices de slots libres,
        /// dans l'ordre des slots. Le caller pioche / mélange selon ses besoins.
        /// </summary>
        public List<int> GetFreeSlotIndices() {
            var list = new List<int>(slots.Count);
            for (int i = 0; i < slots.Count; i++) {
                if (slots[i] != null && !_occupied.ContainsKey(i)) list.Add(i);
            }
            return list;
        }

        /// <summary>
        /// Tente de réserver le slot <paramref name="index"/> pour l'item
        /// <paramref name="entityId"/>. Retourne false si le slot n'existe pas ou est
        /// déjà occupé. Le slot reste réservé jusqu'à <see cref="Release"/>
        /// ou <see cref="ReleaseByEntity"/>.
        /// </summary>
        public bool TryReserve(int index, int entityId) {
            if (GetSlot(index) == null) return false;
            if (_occupied.ContainsKey(index)) return false;
            _occupied[index] = entityId;
            return true;
        }

        /// <summary>Libère le slot d'index donné (no-op si déjà libre).</summary>
        public void Release(int index) {
            _occupied.Remove(index);
        }

        /// <summary>
        /// Libère le slot qui héberge l'item <paramref name="entityId"/>, peu importe
        /// son index. Utile quand on ne suit que l'entityId côté caller.
        /// </summary>
        public void ReleaseByEntity(int entityId) {
            int found = -1;
            foreach (var kv in _occupied) {
                if (kv.Value == entityId) { found = kv.Key; break; }
            }
            if (found >= 0) _occupied.Remove(found);
        }

        /// <summary>Libère toutes les réservations (safety / reset).</summary>
        public void ClearReservations() => _occupied.Clear();

        private void Awake() {
            if (string.IsNullOrEmpty(slotsId)) {
                Debug.LogError($"[JobSpawnSlots] '{name}' has empty slotsId — fix it in the Inspector.");
                return;
            }
            if (_all.ContainsKey(slotsId))
                Debug.LogWarning($"[JobSpawnSlots] duplicate slotsId '{slotsId}' on '{name}'.");
            _all[slotsId] = this;
        }

        private void OnDestroy() {
            if (!string.IsNullOrEmpty(slotsId)
                && _all.TryGetValue(slotsId, out var s) && s == this)
                _all.Remove(slotsId);
        }

        public static JobSpawnSlots Get(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            _all.TryGetValue(id, out var slots);
            return slots;
        }

#if UNITY_EDITOR
        // Visualise les slots dans la scène : sphère = position, rayon = orientation (forward).
        // Couleur indicative selon l'état de réservation EN COURS (utile pour debug en play).
        private void OnDrawGizmos() {
            for (int i = 0; i < slots.Count; i++) {
                if (slots[i] == null) continue;
                Gizmos.color = _occupied.ContainsKey(i) ? Color.red : Color.green;
                Gizmos.DrawWireSphere(slots[i].position, 0.08f);
                Gizmos.DrawRay(slots[i].position, slots[i].forward * 0.2f);
            }
        }
#endif
    }
}
