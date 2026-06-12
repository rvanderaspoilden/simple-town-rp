using Sim.Logging;

namespace Sim.Missions {
    /// <summary>
    /// Dispatcher serveur des récompenses. Souscrit à MissionEvents.MissionCompleted et applique
    /// les récompenses listées par la MissionDefinition. Aucune connaissance du métier —
    /// strategy pattern pur.
    ///
    /// Lit en priorité <c>MissionDefinition.RewardEntries</c> (nouveau modèle Kind+amount
    /// inline). Si la liste est vide, fallback sur <c>MissionDefinition.Rewards</c> (legacy
    /// RewardDefinition par paire SO). Permet aux missions non-migrées de continuer à
    /// fonctionner pendant le passage progressif vers le nouveau système.
    /// </summary>
    public static class RewardSystem {
        private static bool _subscribed;

        public static void Subscribe() {
            if (_subscribed) return;
            MissionEvents.MissionCompleted += OnMissionCompleted;
            _subscribed = true;
        }

        public static void Unsubscribe() {
            if (!_subscribed) return;
            MissionEvents.MissionCompleted -= OnMissionCompleted;
            _subscribed = false;
        }

        private static void OnMissionCompleted(MissionInstance job) {
            if (job?.Definition == null) return;

            // Nouveau modèle : entrées (RewardKind, amount) inline sur la mission.
            var entries = job.Definition.RewardEntries;
            bool anyEntry = false;
            if (entries != null) {
                for (int i = 0; i < entries.Count; i++) {
                    var entry = entries[i];
                    if (entry == null || entry.kind == null || entry.amount <= 0) continue;
                    anyEntry = true;
                    try {
                        entry.kind.Apply(job, entry.amount);
                    } catch (System.Exception ex) {
                        GameLogger.System.Error(ex, "RewardEntryApplyFailed {MissionId} {Kind} {Amount}",
                            job.Definition.MissionId, entry.kind.GetType().Name, entry.amount);
                    }
                }
            }

            // Legacy : si AUCUNE entrée nouveau modèle n'a déclenché, on retombe sur
            // l'ancienne liste de RewardDefinition. Permet aux missions non-migrées de
            // continuer à verser leurs récompenses pendant la transition.
            if (anyEntry) return;
            var legacy = job.Definition.Rewards;
            if (legacy == null) return;
            for (int i = 0; i < legacy.Count; i++) {
                var reward = legacy[i];
                if (reward == null) continue;
                try {
                    reward.Apply(job);
                } catch (System.Exception ex) {
                    GameLogger.System.Error(ex, "RewardApplyFailed {MissionId} {RewardType}",
                        job.Definition.MissionId, reward.GetType().Name);
                }
            }
        }
    }
}
