using Mirror;
using Sim;
using Sim.Entities.Persistence;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Récompense argent. Appliquée serveur via PlayerBankAccount.PostLedger
    /// (round-trip REST : crédit du solde + écriture au registre, raison job_reward).
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Rewards/Money", fileName = "MoneyReward")]
    public class MoneyReward : RewardDefinition {
        [Min(0)]
        [SerializeField] private int amount = 25;

        public int Amount => amount;

        public override string GetDisplayString() => amount > 0 ? $"{amount} €" : string.Empty;

        public override void Apply(MissionInstance job) {
            if (amount <= 0 || job == null) return;
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return;

            var bank = identity != null ? identity.GetComponent<PlayerBankAccount>() : null;
            if (bank == null) {
                GameLogger.System.Warning("MoneyRewardSkipped_NoBank {NetId} {MissionId}",
                    job.OwnerNetId, job.Definition.MissionId);
                return;
            }

            bank.PostLedger(amount, LedgerReason.JobReward, LedgerCounterparty.System, LedgerCounterparty.Job);
            job.AddMoneyEarned(amount);

            var conn = identity.connectionToClient;
            if (conn != null) {
                conn.Send(new MissionRewardNotificationMessage {
                    amount = amount,
                    label = job.Definition.DisplayNameKey ?? job.Definition.MissionId
                });
            }
        }
    }
}
