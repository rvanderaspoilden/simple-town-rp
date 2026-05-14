using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Awards XP on the owner's CharacterJob row for the job's category. The
    /// PlayerController handles persistence (PUT /character-jobs/add-xp) and
    /// the CharacterData rebroadcast.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Rewards/JobXp", fileName = "JobXpReward")]
    public class JobXpReward : RewardDefinition {
        [Min(0)]
        [SerializeField] private int amount = 10;

        public int Amount => amount;

        public override string GetDisplayString() => amount > 0 ? $"+{amount} XP" : string.Empty;

        public override void Apply(JobInstance job) {
            if (amount <= 0 || job == null) return;
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return;

            var player = identity != null ? identity.GetComponent<PlayerController>() : null;
            if (player == null) {
                GameLogger.System.Warning("JobXpRewardSkipped_NoPlayer {NetId} {JobId}",
                    job.OwnerNetId, job.Definition.JobId);
                return;
            }

            player.AddJobXp((int)job.Definition.Category, amount);

            var conn = identity.connectionToClient;
            if (conn != null) {
                conn.Send(new JobNotificationMessage {
                    text = $"+{amount} XP {JobCategoryLabels.Display(job.Definition.Category)}"
                });
            }
        }
    }
}
