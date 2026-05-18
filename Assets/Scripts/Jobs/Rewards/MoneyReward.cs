using Mirror;
using Sim;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Récompense argent. Appliquée serveur via PlayerBankAccount.GiveMoney
    /// (qui round-trip via l'API REST — la persistance backend est gratuite).
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Rewards/Money", fileName = "MoneyReward")]
    public class MoneyReward : RewardDefinition {
        [Min(0)]
        [SerializeField] private int amount = 25;

        public int Amount => amount;

        public override string GetDisplayString() => amount > 0 ? $"{amount} €" : string.Empty;

        public override void Apply(JobInstance job) {
            if (amount <= 0 || job == null) return;
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return;

            var bank = identity != null ? identity.GetComponent<PlayerBankAccount>() : null;
            if (bank == null) {
                GameLogger.System.Warning("MoneyRewardSkipped_NoBank {NetId} {JobId}",
                    job.OwnerNetId, job.Definition.JobId);
                return;
            }

            bank.GiveMoney(amount);

            var conn = identity.connectionToClient;
            if (conn != null) {
                conn.Send(new JobRewardNotificationMessage {
                    amount = amount,
                    label = job.Definition.DisplayNameKey ?? job.Definition.JobId
                });
            }
        }
    }
}
