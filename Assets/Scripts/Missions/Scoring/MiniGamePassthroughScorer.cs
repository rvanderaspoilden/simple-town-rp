using Sim.SubGames.Packaging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Lit le rating du mini-jeu d'emballage stocké dans le contexte par
    /// UseMachineStepInstance et le convertit en MissionRating.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Scoring/MiniGame Passthrough", fileName = "MiniGamePassthroughScorer")]
    public class MiniGamePassthroughScorer : MissionScoringDefinition {
        public override MissionRating Evaluate(MissionInstance job) {
            if (!job.Context.TryGetStruct<int>(UseMachineStepInstance.CtxRatingKey, out var ratingInt))
                return MissionRating.Ok;

            return (PackageRating)ratingInt switch {
                PackageRating.Perfect => MissionRating.Perfect,
                PackageRating.Good    => MissionRating.Good,
                _                    => MissionRating.Ok
            };
        }
    }
}
