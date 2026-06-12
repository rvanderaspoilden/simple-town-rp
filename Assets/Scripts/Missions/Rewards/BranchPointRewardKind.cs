using System.Collections.Generic;
using Mirror;
using Sim.Constellation.Branches;
using Sim.Logging;
using Sim.Player;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Crédite N points dans la devise d'une branche constellation (Créatif / Sportif /
    /// Sociable / Ingénieux). La branche cible est portée par le SO <see cref="branch"/>
    /// — un asset générique <c>BranchPointReward.asset</c> peut être dupliqué par branche
    /// (BranchPointReward_Sociable, BranchPointReward_Creatif, …) et référencé dans les
    /// missions qui veulent crédite chacune.
    ///
    /// Contrairement à <see cref="OwnProfessionPointRewardKind"/> qui dérive la
    /// profession depuis <c>mission.Definition.Profession</c>, ici la branche est
    /// explicite. Cela permet à n'importe quelle mission de crédite n'importe quelle
    /// branche, sans dépendance au métier de la mission.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Reward Kinds/Branch Points", fileName = "BranchPointReward")]
    public class BranchPointRewardKind : RewardKind {
        [Tooltip("Branche à créditer. Le label et la couleur viennent du SO BranchConfig ; " +
                 "la clé JSONB envoyée sur le fil backend est branch.id.")]
        [SerializeField] private BranchConfig branch;

        public override string GetDisplayString(int authoredAmount) {
            if (branch == null || authoredAmount <= 0) return string.Empty;
            string label = string.IsNullOrEmpty(branch.displayName) ? branch.id : branch.displayName;
            return $"+{authoredAmount} {label}";
        }

        public override void Apply(MissionInstance mission, int authoredAmount) {
            if (mission == null || authoredAmount <= 0) return;
            if (branch == null) {
                GameLogger.System.Warning("BranchRewardSkipped_NoBranch {MissionId}", mission.Definition?.MissionId);
                return;
            }
            if (!NetworkServer.spawned.TryGetValue(mission.OwnerNetId, out var identity)) return;

            var pc = identity != null ? identity.GetComponent<PlayerConstellation>() : null;
            if (pc == null) {
                GameLogger.System.Warning("BranchRewardSkipped_NoComponent {NetId} {MissionId}",
                    mission.OwnerNetId, mission.Definition.MissionId);
                return;
            }

            string branchKey = branch.id;
            string label = string.IsNullOrEmpty(branch.displayName) ? branchKey : branch.displayName;
            mission.AddBranchPointsEarned(branchKey, authoredAmount, label);

            GameLogger.System.Info("BranchRewardApplied {NetId} {MissionId} {Branch} {Amount}",
                mission.OwnerNetId, mission.Definition.MissionId, branchKey, authoredAmount);

            var dict = new Dictionary<string, int> { { branchKey, authoredAmount } };
            pc.GrantPoints(dict,
                $"mission_reward:{(mission.Definition != null ? mission.Definition.MissionId : "unknown")}");
        }
    }
}
