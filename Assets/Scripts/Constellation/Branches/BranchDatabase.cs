using System.Collections.Generic;
using UnityEngine;

namespace Sim.Constellation.Branches {
    /// <summary>
    /// Annuaire statique des <see cref="BranchConfig"/> chargés depuis
    /// <c>Resources/Configurations/Constellation/Branches/</c>. Lazy-loaded à la première utilisation.
    ///
    /// Source unique de vérité pour les devises de la constellation. Chaque branche
    /// (racine OU sous-branche) est une devise distincte keyée par <see cref="BranchConfig.id"/>.
    /// La hiérarchie <see cref="BranchConfig.parent"/> est purement visuelle (layout +
    /// regroupement profil) et n'influence pas le stockage des points.
    ///
    /// Tous les lookups sont O(1) une fois l'index construit.
    /// </summary>
    public static class BranchDatabase {

        private const string ResourcesPath = "Configurations/Constellation/Branches";

        private static List<BranchConfig> _all;
        private static Dictionary<string, BranchConfig> _byId;
        private static List<BranchConfig> _roots;

        public static IReadOnlyList<BranchConfig> All { get { Ensure(); return _all; } }

        /// <summary>Lookup par id de devise (clé canonique runtime + wire).</summary>
        public static BranchConfig ById(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            Ensure();
            return _byId.TryGetValue(id, out var b) ? b : null;
        }

        /// <summary>Branches racines (parent == null), dans l'ordre de chargement.
        /// Sert au layout angulaire et aux compteurs de profil.</summary>
        public static IReadOnlyList<BranchConfig> RootBranches() { Ensure(); return _roots; }

        /// <summary>Index de la branche racine parmi <see cref="RootBranches"/> (-1 si absente).
        /// Pour une sous-branche, remonter d'abord via <see cref="TopLevelAncestor"/>.</summary>
        public static int IndexOfRoot(BranchConfig root) {
            Ensure();
            return root == null ? -1 : _roots.IndexOf(root);
        }

        /// <summary>Remonte la chaîne <c>parent</c> jusqu'à la branche racine. Garde-fou
        /// anti-cycle (plafond d'itérations). Renvoie <paramref name="b"/> si déjà racine.</summary>
        public static BranchConfig TopLevelAncestor(BranchConfig b) {
            int guard = 0;
            while (b != null && b.parent != null && guard++ < 32) b = b.parent;
            return b;
        }

        /// <summary>
        /// Force un reload depuis Resources. Utile après le générateur d'éditeur ou un
        /// changement d'asset en cours de session.
        /// </summary>
        public static void Reload() {
            _all = null;
            _byId = null;
            _roots = null;
            Ensure();
        }

        private static void Ensure() {
            if (_all != null) return;
            var loaded = Resources.LoadAll<BranchConfig>(ResourcesPath);
            _all = new List<BranchConfig>(loaded.Length);
            _byId = new Dictionary<string, BranchConfig>(loaded.Length);
            _roots = new List<BranchConfig>();
            for (int i = 0; i < loaded.Length; i++) {
                var b = loaded[i];
                if (b == null) continue;
                _all.Add(b);
                if (!string.IsNullOrEmpty(b.id)) {
                    if (_byId.ContainsKey(b.id))
                        Debug.LogWarning($"[BranchDatabase] Duplicate branch id '{b.id}' on {b.name} (also on {_byId[b.id].name})");
                    else
                        _byId[b.id] = b;
                }
                if (b.parent == null) _roots.Add(b);
            }
        }
    }
}
