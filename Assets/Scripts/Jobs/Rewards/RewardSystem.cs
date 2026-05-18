using Sim.Logging;

namespace Sim.Jobs {
    /// <summary>
    /// Dispatcher serveur des récompenses. Souscrit à JobEvents.JobCompleted
    /// et applique chaque RewardDefinition listée par la JobDefinition.
    /// Aucune connaissance du métier — strategy pattern pur.
    /// </summary>
    public static class RewardSystem {
        private static bool _subscribed;

        public static void Subscribe() {
            if (_subscribed) return;
            JobEvents.JobCompleted += OnJobCompleted;
            _subscribed = true;
        }

        public static void Unsubscribe() {
            if (!_subscribed) return;
            JobEvents.JobCompleted -= OnJobCompleted;
            _subscribed = false;
        }

        private static void OnJobCompleted(JobInstance job) {
            if (job?.Definition?.Rewards == null) return;

            foreach (var reward in job.Definition.Rewards) {
                if (reward == null) continue;
                try {
                    reward.Apply(job);
                } catch (System.Exception ex) {
                    GameLogger.System.Error(ex, "RewardApplyFailed {JobId} {RewardType}",
                        job.Definition.JobId, reward.GetType().Name);
                }
            }
        }
    }
}
