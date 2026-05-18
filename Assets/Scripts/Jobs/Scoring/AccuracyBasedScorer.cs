using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Score basé sur le ratio de tris corrects écrit dans le contexte par
    /// SortItemsStepInstance. Compatible avec toute mission qui stocke un
    /// float "sortAccuracyRatio" (0–1) dans son JobContext.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Scoring/Accuracy Based", fileName = "AccuracyBasedScorer")]
    public class AccuracyBasedScorer : JobScoringDefinition {
        [Tooltip("Ratio minimum pour Perfect (ex. 1.0 = 100 % corrects).")]
        [Range(0f, 1f)] [SerializeField] private float perfectThreshold = 1.0f;

        [Tooltip("Ratio minimum pour Good.")]
        [Range(0f, 1f)] [SerializeField] private float goodThreshold = 0.8f;

        [Tooltip("Ratio minimum pour Ok. En-dessous : Poor.")]
        [Range(0f, 1f)] [SerializeField] private float okThreshold = 0.6f;

        public override JobResultVariant ResultVariant => JobResultVariant.Sort;

        public override JobRating Evaluate(JobInstance job) {
            if (!job.Context.TryGetStruct<float>(SortItemsStepInstance.CtxAccuracyKey, out var ratio))
                return JobRating.Perfect;

            if (ratio >= perfectThreshold) return JobRating.Perfect;
            if (ratio >= goodThreshold)    return JobRating.Good;
            if (ratio >= okThreshold)      return JobRating.Ok;
            return JobRating.Poor;
        }
    }
}
