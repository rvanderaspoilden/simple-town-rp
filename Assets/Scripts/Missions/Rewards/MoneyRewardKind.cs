using Mirror;
using Sim;
using Sim.Entities.Persistence;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Récompense argent générique (un asset unique partagé par toutes les missions).
    /// Crédit posté côté serveur via <see cref="PlayerBankAccount.PostLedger"/> avec
    /// le montant authoré sur la <see cref="RewardEntry"/>. Comportement identique à
    /// l'ancien <see cref="MoneyReward"/>, sauf que <c>amount</c> vient maintenant
    /// du paramètre <paramref name="authoredAmount"/> au lieu d'un champ SO.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Reward Kinds/Money", fileName = "MoneyReward")]
    public class MoneyRewardKind : RewardKind {
        public override string GetDisplayString(int authoredAmount) =>
            authoredAmount > 0 ? $"{authoredAmount} €" : string.Empty;

        public override void Apply(MissionInstance job, int authoredAmount) {
            if (authoredAmount <= 0 || job == null) return;
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return;

            var bank = identity != null ? identity.GetComponent<PlayerBankAccount>() : null;
            if (bank == null) {
                GameLogger.System.Warning("MoneyRewardSkipped_NoBank {NetId} {MissionId}",
                    job.OwnerNetId, job.Definition.MissionId);
                return;
            }

            // Bonus de gains issus des nœuds passifs de constellation (ex. Pourboires,
            // Maître Livreur). 1.0 si aucun bonus actif.
            var pc = identity.GetComponent<Sim.Player.PlayerConstellation>();
            float mult = Sim.Constellation.ConstellationPerks.EarningsMultiplier(pc, job.Definition.ProfessionId);
            int amount = UnityEngine.Mathf.RoundToInt(authoredAmount * mult);
            if (amount <= 0) return;

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
