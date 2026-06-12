using System.Collections.Generic;
using UnityEngine;

namespace Sim.Professions {
    /// <summary>
    /// Annuaire statique des <see cref="ProfessionConfig"/> chargés depuis
    /// <c>Resources/Configurations/Professions/</c>. Lazy-loaded à la première utilisation.
    ///
    /// Lookup par id (string, ex "delivery_driver") — clé canonique runtime + wire (DB).
    /// Liste complète pour le code éditeur. Tous les lookups sont O(1).
    /// </summary>
    public static class ProfessionDatabase {

        private const string ResourcesPath = "Configurations/Professions";

        private static List<ProfessionConfig> _all;
        private static Dictionary<string, ProfessionConfig> _byId;

        public static IReadOnlyList<ProfessionConfig> All { get { Ensure(); return _all; } }

        public static ProfessionConfig ById(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            Ensure();
            return _byId.TryGetValue(id, out var p) ? p : null;
        }

        /// <summary>
        /// Force un reload depuis Resources. Utile après le générateur d'éditeur ou un
        /// changement d'asset en cours de session.
        /// </summary>
        public static void Reload() {
            _all = null;
            _byId = null;
            Ensure();
        }

        private static void Ensure() {
            if (_all != null) return;
            var loaded = Resources.LoadAll<ProfessionConfig>(ResourcesPath);
            _all = new List<ProfessionConfig>(loaded.Length);
            _byId = new Dictionary<string, ProfessionConfig>(loaded.Length);
            for (int i = 0; i < loaded.Length; i++) {
                var p = loaded[i];
                if (p == null) continue;
                _all.Add(p);
                if (!string.IsNullOrEmpty(p.id)) {
                    if (_byId.ContainsKey(p.id)) {
                        Debug.LogWarning($"[ProfessionDatabase] Duplicate id '{p.id}' on {p.name} (also on {_byId[p.id].name})");
                    } else {
                        _byId[p.id] = p;
                    }
                }
            }
        }
    }
}
