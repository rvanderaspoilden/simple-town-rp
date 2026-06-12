using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using Sim.Player;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Crédite N points dans la devise de la <b>profession propre</b> de la mission.
    /// La profession N'EST PAS un champ du SO : elle est résolue dynamiquement
    /// depuis <c>mission.Definition.Profession</c>. Conséquence : un asset unique
    /// <c>OwnProfessionPointReward.asset</c> sert pour toutes les missions, et
    /// chaque mission crédite automatiquement la bonne devise (Livreur pour les
    /// missions de livraison, Réparateur pour la réparation, etc.).
    ///
    /// Trio cohérent avec les deux autres reward kinds constellation :
    ///   - <c>OwnProfessionPointRewardKind</c> (ce fichier) : profession DE la mission, auto ;
    ///   - <see cref="ProfessionPointRewardKind"/> : profession explicite (autre métier) ;
    ///   - <see cref="BranchPointRewardKind"/> : branche explicite (Créatif / Sportif / …).
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Reward Kinds/Own Profession Points", fileName = "OwnProfessionPointReward")]
    public class OwnProfessionPointRewardKind : RewardKind {

        public override string GetDisplayString(int authoredAmount) =>
            authoredAmount > 0 ? $"+{authoredAmount} pts" : string.Empty;

        public override void Apply(MissionInstance mission, int authoredAmount) {
            if (mission == null || authoredAmount <= 0) return;
            var profession = mission.Definition?.Profession;
            if (profession == null || string.IsNullOrEmpty(profession.id)) {
                GameLogger.System.Warning("OwnProfessionRewardSkipped_NoProfession {MissionId}",
                    mission.Definition?.MissionId);
                return;
            }
            if (!NetworkServer.spawned.TryGetValue(mission.OwnerNetId, out var identity)) return;

            var pc = identity != null ? identity.GetComponent<PlayerConstellation>() : null;
            if (pc == null) {
                GameLogger.System.Warning("OwnProfessionRewardSkipped_NoComponent {NetId} {MissionId}",
                    mission.OwnerNetId, mission.Definition.MissionId);
                return;
            }

            string profLabel = string.IsNullOrEmpty(profession.displayName) ? profession.id : profession.displayName;
            mission.AddProfessionPointsEarned(profession.id, authoredAmount, profLabel);

            GameLogger.System.Info("OwnProfessionRewardApplied {NetId} {MissionId} {Profession} {Amount}",
                mission.OwnerNetId, mission.Definition.MissionId, profession.id, authoredAmount);

            var dict = new Dictionary<string, int> { { profession.id, authoredAmount } };
            pc.GrantPoints(dict,
                $"mission_reward:{(mission.Definition != null ? mission.Definition.MissionId : "unknown")}");
        }
    }
}
