using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using Sim.Player;
using Sim.Professions;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Constellation reward attached to a MissionDefinition. Credits N profession
    /// points on the player who just completed the job. Server-side (Mirror
    /// [Server] entry point via RewardSystem.OnMissionCompleted) — routes through
    /// PlayerConstellation.GrantPoints which posts to the REST chokepoint.
    ///
    /// Note : branch currencies (Créatif / Sportif / Sociable / Ingénieux) are
    /// not credited by missions. Only the per-profession wallet is awarded ;
    /// branch wallets exist for future hybrid nodes but no production path
    /// fills them today.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Rewards/Constellation Points", fileName = "ConstellationPointReward")]
    public class ConstellationPointReward : RewardDefinition {

        [Header("Profession credit")]
        [Tooltip("Métier crédité. Le label et la couleur viennent du SO ProfessionConfig ; l'id " +
                 "est envoyé sur le fil backend (clé JSONB profession_points).")]
        [SerializeField] private ProfessionConfig profession;
        [Min(0)] [SerializeField] private int professionAmount = 0;

        // ── LEGACY ────────────────────────────────────────────────────────────
        // Champ conservé UNIQUEMENT pour la migration des assets existants
        // (YAML key professionId). Aucune logique runtime ne le lit.
        [HideInInspector] [SerializeField] private string professionId = "";
        public string LegacyProfessionId => professionId;

        public override string GetDisplayString() {
            if (profession == null || professionAmount <= 0) return string.Empty;
            string label = string.IsNullOrEmpty(profession.displayName) ? profession.id : profession.displayName;
            return $"+{professionAmount} {label}";
        }

        public override void Apply(MissionInstance job) {
            if (job == null) return;
            if (profession == null || professionAmount <= 0) return;
            if (string.IsNullOrEmpty(profession.id)) {
                GameLogger.System.Warning("ConstellationRewardSkipped_NoId {MissionId}", job.Definition?.MissionId);
                return;
            }
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return;

            var pc = identity != null ? identity.GetComponent<PlayerConstellation>() : null;
            if (pc == null) {
                GameLogger.System.Warning("ConstellationRewardSkipped_NoComponent {NetId} {MissionId}",
                    job.OwnerNetId, job.Definition.MissionId);
                return;
            }

            string profLabel = string.IsNullOrEmpty(profession.displayName) ? profession.id : profession.displayName;
            job.AddProfessionPointsEarned(profession.id, professionAmount, profLabel);

            GameLogger.System.Info("ConstellationRewardApplied {NetId} {MissionId} {Profession} {ProfessionAmount}",
                job.OwnerNetId, job.Definition.MissionId, profession.id, professionAmount);

            var professionDict = new Dictionary<string, int> { { profession.id, professionAmount } };
            pc.GrantPoints(professionDict,
                $"job_reward:{(job.Definition != null ? job.Definition.MissionId : "unknown")}");
        }
    }
}
