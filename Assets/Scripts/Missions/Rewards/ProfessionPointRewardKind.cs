using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using Sim.Player;
using Sim.Professions;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Crédite N points dans la devise d'une profession <b>explicite</b>. La profession
    /// cible est portée par le SO <see cref="profession"/> — un asset générique
    /// <c>ProfessionPointReward.asset</c> peut être dupliqué par profession
    /// (ProfessionPointReward_Livreur, ProfessionPointReward_Reparateur, …) et référencé
    /// dans les missions qui veulent crédite une profession DIFFÉRENTE de la leur.
    ///
    /// Contrairement à <see cref="OwnProfessionPointRewardKind"/> qui dérive la
    /// profession depuis <c>mission.Definition.Profession</c>, ici elle est explicite.
    /// Utile pour les missions « bonus inter-métiers » (ex. une mission de livraison qui
    /// donne aussi des points Réparation parce qu'elle a impliqué de réparer un colis
    /// abîmé), ou pour les missions sans profession assignée.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Reward Kinds/Profession Points (Explicit)", fileName = "ProfessionPointReward")]
    public class ProfessionPointRewardKind : RewardKind {
        [Tooltip("Profession à crédite. Le label et la couleur viennent du SO ProfessionConfig ; " +
                 "la clé JSONB envoyée sur le fil backend est profession.id.")]
        [SerializeField] private ProfessionConfig profession;

        public override string GetDisplayString(int authoredAmount) {
            if (profession == null || authoredAmount <= 0) return string.Empty;
            string label = string.IsNullOrEmpty(profession.displayName) ? profession.id : profession.displayName;
            return $"+{authoredAmount} {label}";
        }

        public override void Apply(MissionInstance mission, int authoredAmount) {
            if (mission == null || authoredAmount <= 0) return;
            if (profession == null || string.IsNullOrEmpty(profession.id)) {
                GameLogger.System.Warning("ProfessionRewardExplicitSkipped_NoProfession {MissionId}",
                    mission.Definition?.MissionId);
                return;
            }
            if (!NetworkServer.spawned.TryGetValue(mission.OwnerNetId, out var identity)) return;

            var pc = identity != null ? identity.GetComponent<PlayerConstellation>() : null;
            if (pc == null) {
                GameLogger.System.Warning("ProfessionRewardExplicitSkipped_NoComponent {NetId} {MissionId}",
                    mission.OwnerNetId, mission.Definition.MissionId);
                return;
            }

            string label = string.IsNullOrEmpty(profession.displayName) ? profession.id : profession.displayName;
            mission.AddProfessionPointsEarned(profession.id, authoredAmount, label);

            GameLogger.System.Info("ProfessionRewardExplicitApplied {NetId} {MissionId} {Profession} {Amount}",
                mission.OwnerNetId, mission.Definition.MissionId, profession.id, authoredAmount);

            var dict = new Dictionary<string, int> { { profession.id, authoredAmount } };
            pc.GrantPoints(dict,
                $"mission_reward:{(mission.Definition != null ? mission.Definition.MissionId : "unknown")}");
        }
    }
}
