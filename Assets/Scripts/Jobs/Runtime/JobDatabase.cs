using System.Collections.Generic;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Registre statique des JobDefinition chargées au démarrage depuis
    /// Resources/Configurations/Jobs/Definitions/**. Permet aux clients de
    /// retrouver une définition par jobId à partir d'un JobOfferedMessage,
    /// et aux providers serveur d'instancier une mission par jobId.
    /// </summary>
    public static class JobDatabase {
        private static readonly Dictionary<string, JobDefinition> _byId =
            new Dictionary<string, JobDefinition>();

        public static bool Loaded { get; private set; }

        public static void Load() {
            if (Loaded) return;
            _byId.Clear();

            var all = Resources.LoadAll<JobDefinition>("Configurations/Jobs/Definitions");
            foreach (var def in all) {
                if (def == null || string.IsNullOrEmpty(def.JobId)) continue;
                if (_byId.ContainsKey(def.JobId)) {
                    GameLogger.System.Warning("JobDatabaseDuplicateJobId {JobId}", def.JobId);
                    continue;
                }
                _byId[def.JobId] = def;
            }
            Loaded = true;
            GameLogger.System.Info("JobDatabaseLoaded {Count}", _byId.Count);
        }

        public static JobDefinition GetById(string jobId) {
            if (string.IsNullOrEmpty(jobId)) return null;
            return _byId.TryGetValue(jobId, out var def) ? def : null;
        }

        public static IEnumerable<JobDefinition> All => _byId.Values;

        /// <summary>
        /// Salary lookup for the career system. Resolves the first JobDefinition
        /// whose category matches and returns its SalaryAmount. Returns 0 if no
        /// definition exists for that category. POC compromise: if multiple
        /// definitions share a category, only the first one's salary is used.
        /// </summary>
        public static int GetSalaryForCategory(JobCategory category) {
            foreach (var def in _byId.Values) {
                if (def != null && def.Category == category) return def.SalaryAmount;
            }
            return 0;
        }
    }
}
