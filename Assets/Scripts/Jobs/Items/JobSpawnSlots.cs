using System;
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

        public string SlotsId => slotsId;
        public int SlotCount => slots.Count;

        /// <summary>Slot d'index donné, ou null si hors limites / non assigné.</summary>
        public Transform GetSlot(int index)
            => index >= 0 && index < slots.Count ? slots[index] : null;

        /// <summary>
        /// Choisit un slot au hasard. <paramref name="isAvailable"/> filtre les slots
        /// disponibles (ex. aucun item posé dessus) ; si fourni et qu'au moins un slot
        /// passe le test, le tirage se fait parmi ceux-là (<paramref name="wasFree"/> = true).
        /// Si aucun n'est disponible, repli sur un tirage parmi TOUS les slots non-null
        /// (<paramref name="wasFree"/> = false) pour ne pas bloquer le spawn. Retourne null
        /// seulement si aucun slot n'est assigné. <paramref name="index"/> = index choisi (ou -1).
        /// </summary>
        public Transform GetRandomSlot(Func<Transform, bool> isAvailable, out int index, out bool wasFree) {
            index = -1;
            wasFree = false;
            if (slots == null || slots.Count == 0) return null;

            var free = new List<int>();
            var all = new List<int>();
            for (int i = 0; i < slots.Count; i++) {
                if (slots[i] == null) continue;
                all.Add(i);
                if (isAvailable == null || isAvailable(slots[i])) free.Add(i);
            }
            if (all.Count == 0) return null;

            if (free.Count > 0) {
                index = free[UnityEngine.Random.Range(0, free.Count)];
                wasFree = true;
            } else {
                index = all[UnityEngine.Random.Range(0, all.Count)];
                wasFree = false;
            }
            return slots[index];
        }

        /// <summary>
        /// Ordre aléatoire d'indices de slots, **disponibles d'abord** (mélangés) puis les
        /// occupés (mélangés) en repli. <paramref name="isAvailable"/> filtre la dispo (ex.
        /// aucun item posé dessus). Sert à répartir N items sur des slots LIBRES DISTINCTS
        /// au hasard (ex. SortItemsStep avec plus de slots que de colis). La longueur = nombre
        /// de slots non-null ; le caller prend les N premiers.
        /// </summary>
        public List<int> GetShuffledSlotOrder(Func<Transform, bool> isAvailable) {
            var available = new List<int>();
            var occupied = new List<int>();
            if (slots != null) {
                for (int i = 0; i < slots.Count; i++) {
                    if (slots[i] == null) continue;
                    if (isAvailable == null || isAvailable(slots[i])) available.Add(i);
                    else occupied.Add(i);
                }
            }
            Shuffle(available);
            Shuffle(occupied);
            available.AddRange(occupied); // libres d'abord, occupés en dernier recours
            return available;
        }

        private static void Shuffle(List<int> list) {
            for (int i = list.Count - 1; i > 0; i--) {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

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
        private void OnDrawGizmos() {
            Gizmos.color = Color.green;
            for (int i = 0; i < slots.Count; i++) {
                if (slots[i] == null) continue;
                Gizmos.DrawWireSphere(slots[i].position, 0.08f);
                Gizmos.DrawRay(slots[i].position, slots[i].forward * 0.2f);
            }
        }
#endif
    }
}
