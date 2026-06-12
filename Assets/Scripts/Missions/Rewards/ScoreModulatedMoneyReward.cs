using Mirror;
using Sim;
using Sim.Entities.Persistence;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Récompense argent dont le montant est modulé par le MissionRating de la
    /// mission. Si aucune règle de scoring n'est configurée sur le job, le
    /// montant de base est versé intégralement (équivalent Perfect).
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Rewards/Score Modulated Money", fileName = "ScoreModulatedMoneyReward")]
    public class ScoreModulatedMoneyReward : RewardDefinition {
        [Min(0)]
        [SerializeField] private int baseAmount = 50;

        [Range(0f, 1f)] [SerializeField] private float perfectMultiplier = 1.00f;
        [Range(0f, 1f)] [SerializeField] private float goodMultiplier    = 0.80f;
        [Range(0f, 1f)] [SerializeField] private float okMultiplier      = 0.60f;
        [Range(0f, 1f)] [SerializeField] private float poorMultiplier    = 0.30f;

        public override string GetDisplayString() {
            int min = Mathf.RoundToInt(baseAmount * poorMultiplier);
            int max = Mathf.RoundToInt(baseAmount * perfectMultiplier);
            return min == max ? $"{max} €" : $"{min}-{max} €";
        }

        public override void Apply(MissionInstance job) {
            if (baseAmount <= 0 || job == null) return;
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return;

            var bank = identity != null ? identity.GetComponent<PlayerBankAccount>() : null;
            if (bank == null) {
                GameLogger.System.Warning("ScoreModulatedRewardSkipped_NoBank {NetId} {MissionId}",
                    job.OwnerNetId, job.Definition.MissionId);
                return;
            }

            var scoring = job.Definition.ScoringDefinition;
            var rating  = scoring != null ? scoring.Evaluate(job) : MissionRating.Perfect;
            int amount  = Mathf.RoundToInt(baseAmount * MultiplierFor(rating));

            if (amount <= 0) return;

            bank.PostLedger(amount, LedgerReason.JobReward, LedgerCounterparty.System, LedgerCounterparty.Job);
            job.AddMoneyEarned(amount);

            var conn = identity.connectionToClient;
            if (conn != null) {
                conn.Send(new MissionRewardNotificationMessage {
                    amount = amount,
                    label  = job.Definition.DisplayNameKey ?? job.Definition.MissionId
                });
            }

            GameLogger.System.Info("ScoreModulatedReward_Applied {NetId} {MissionId} {Rating} {Amount}",
                job.OwnerNetId, job.Definition.MissionId, rating, amount);
        }

        private float MultiplierFor(MissionRating rating) => rating switch {
            MissionRating.Perfect => perfectMultiplier,
            MissionRating.Good    => goodMultiplier,
            MissionRating.Ok      => okMultiplier,
            MissionRating.Poor    => poorMultiplier,
            _                 => perfectMultiplier
        };
    }
}
