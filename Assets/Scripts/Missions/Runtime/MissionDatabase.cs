using System.Collections.Generic;
using Sim.Logging;
using Sim.Professions;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Registre statique des MissionDefinition chargées au démarrage depuis
    /// Resources/Configurations/Missions/Definitions/**. Permet aux clients de
    /// retrouver une définition par missionId à partir d'un MissionOfferedMessage,
    /// et aux providers serveur d'instancier une mission par missionId.
    /// </summary>
    public static class MissionDatabase {
        private static readonly Dictionary<string, MissionDefinition> _byId =
            new Dictionary<string, MissionDefinition>();

        public static bool Loaded { get; private set; }

        public static void Load() {
            if (Loaded) return;
            _byId.Clear();

            var all = Resources.LoadAll<MissionDefinition>("Configurations/Missions/Definitions");
            foreach (var def in all) {
                if (def == null || string.IsNullOrEmpty(def.MissionId)) continue;
                if (_byId.ContainsKey(def.MissionId)) {
                    GameLogger.System.Warning("MissionDatabaseDuplicateJobId {MissionId}", def.MissionId);
                    continue;
                }
                _byId[def.MissionId] = def;
            }
            Loaded = true;
            GameLogger.System.Info("MissionDatabaseLoaded {Count}", _byId.Count);
        }

        public static MissionDefinition GetById(string missionId) {
            if (string.IsNullOrEmpty(missionId)) return null;
            return _byId.TryGetValue(missionId, out var def) ? def : null;
        }

        public static IEnumerable<MissionDefinition> All => _byId.Values;

        /// <summary>
        /// Salary lookup for the career system. Délègue à ProfessionDatabase : le salaire
        /// vit désormais sur l'asset ProfessionConfig de chaque métier, pas sur les
        /// MissionDefinitions. Cela règle l'ancien bug « first match wins » qui choisissait
        /// le premier MissionDefinition matchant la catégorie quand plusieurs missions
        /// partageaient un métier.
        /// </summary>
        public static int GetSalaryForProfession(string professionId) {
            var profession = ProfessionDatabase.ById(professionId);
            return profession != null ? profession.baseSalary : 0;
        }
    }
}
