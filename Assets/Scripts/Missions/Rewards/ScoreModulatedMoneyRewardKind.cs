using Mirror;
using Sim;
using Sim.Entities.Persistence;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Récompense argent dont le montant est modulé par le rating de la mission.
    /// Le montant authoré sur la <see cref="RewardEntry"/> est traité comme « payout
    /// max » (rating Perfect = 100 %) ; les autres ratings reçoivent une fraction
    /// configurée sur le SO.
    ///
    /// Exemple de calcul dynamique : si on voulait scaler en fonction du niveau du
    /// joueur, il suffirait de surclasser <see cref="Apply"/> ou d'ajouter un champ
    /// <c>levelBonus</c> ici. C'est tout l'intérêt de séparer le KIND du AMOUNT.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Reward Kinds/Score Modulated Money", fileName = "ScoreModulatedMoneyReward")]
    public class ScoreModulatedMoneyRewardKind : RewardKind {
        [Range(0f, 1f)] [SerializeField] private float perfectMultiplier = 1.00f;
        [Range(0f, 1f)] [SerializeField] private float goodMultiplier    = 0.80f;
        [Range(0f, 1f)] [SerializeField] private float okMultiplier      = 0.60f;
        [Range(0f, 1f)] [SerializeField] private float poorMultiplier    = 0.30f;

        public override string GetDisplayString(int authoredAmount) {
            if (authoredAmount <= 0) return string.Empty;
            int min = Mathf.RoundToInt(authoredAmount * poorMultiplier);
            int max = Mathf.RoundToInt(authoredAmount * perfectMultiplier);
            return min == max ? $"{max} €" : $"{min}-{max} €";
        }

        public override void Apply(MissionInstance job, int authoredAmount) {
            if (authoredAmount <= 0 || job == null) return;
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return;

            var bank = identity != null ? identity.GetComponent<PlayerBankAccount>() : null;
            if (bank == null) {
                GameLogger.System.Warning("ScoreModulatedRewardSkipped_NoBank {NetId} {MissionId}",
                    job.OwnerNetId, job.Definition.MissionId);
                return;
            }

            var scoring = job.Definition.ScoringDefinition;
            var rating  = scoring != null ? scoring.Evaluate(job) : MissionRating.Perfect;

            // Bonus de gains issus des nœuds passifs de constellation (Pourboires,
            // Maître Livreur) appliqué APRÈS la modulation par le rating. La Prime de
            // rapidité s'ajoute en plus, proportionnelle au rating (= vitesse pour une
            // mission à scoring temporel).
            var pc = identity.GetComponent<Sim.Player.PlayerConstellation>();
            string profId = job.Definition.ProfessionId;
            float perkMult = Sim.Constellation.ConstellationPerks.EarningsMultiplier(pc, profId);
            if (pc != null) {
                float speedFraction = RatingSpeedFraction(rating);
                perkMult += Sim.Constellation.ConstellationPerks.RushEarningsBonus(
                    profId, speedFraction, pc.ServerHasUnlockedNode);
            }
            int amount  = Mathf.RoundToInt(authoredAmount * MultiplierFor(rating) * perkMult);

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

        // Fraction de vitesse [0,1] dérivée du rating, pour doser la Prime de rapidité.
        // Seules les livraisons rapides (Perfect/Good) en profitent réellement.
        private static float RatingSpeedFraction(MissionRating rating) => rating switch {
            MissionRating.Perfect => 1f,
            MissionRating.Good    => 0.5f,
            _                     => 0f
        };

        private float MultiplierFor(MissionRating rating) => rating switch {
            MissionRating.Perfect => perfectMultiplier,
            MissionRating.Good    => goodMultiplier,
            MissionRating.Ok      => okMultiplier,
            MissionRating.Poor    => poorMultiplier,
            _                 => perfectMultiplier
        };
    }
}
