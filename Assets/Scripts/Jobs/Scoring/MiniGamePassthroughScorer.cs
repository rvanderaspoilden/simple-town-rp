using Sim.SubGames.Packaging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Lit le rating du mini-jeu d'emballage stocké dans le contexte par
    /// UseMachineStepInstance et le convertit en JobRating.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Scoring/MiniGame Passthrough", fileName = "MiniGamePassthroughScorer")]
    public class MiniGamePassthroughScorer : JobScoringDefinition {
        public override JobResultVariant ResultVariant => JobResultVariant.MiniGame;

        public override JobRating Evaluate(JobInstance job) {
            if (!job.Context.TryGetStruct<int>(UseMachineStepInstance.CtxRatingKey, out var ratingInt))
                return JobRating.Ok;

            return (PackageRating)ratingInt switch {
                PackageRating.Perfect => JobRating.Perfect,
                PackageRating.Good    => JobRating.Good,
                _                    => JobRating.Ok
            };
        }
    }
}
