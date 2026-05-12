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
    }
}
