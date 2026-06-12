using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Score basé sur le ratio de tris corrects écrit dans le contexte par
    /// SortItemsStepInstance. Compatible avec toute mission qui stocke un
    /// float "sortAccuracyRatio" (0–1) dans son MissionContext.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Scoring/Accuracy Based", fileName = "AccuracyBasedScorer")]
    public class AccuracyBasedScorer : MissionScoringDefinition {
        [Tooltip("Ratio minimum pour Perfect (ex. 1.0 = 100 % corrects).")]
        [Range(0f, 1f)] [SerializeField] private float perfectThreshold = 1.0f;

        [Tooltip("Ratio minimum pour Good.")]
        [Range(0f, 1f)] [SerializeField] private float goodThreshold = 0.8f;

        [Tooltip("Ratio minimum pour Ok. En-dessous : Poor.")]
        [Range(0f, 1f)] [SerializeField] private float okThreshold = 0.6f;

        public override MissionRating Evaluate(MissionInstance job) {
            if (!job.Context.TryGetStruct<float>(SortItemsStepInstance.CtxAccuracyKey, out var ratio))
                return MissionRating.Perfect;

            if (ratio >= perfectThreshold) return MissionRating.Perfect;
            if (ratio >= goodThreshold)    return MissionRating.Good;
            if (ratio >= okThreshold)      return MissionRating.Ok;
            return MissionRating.Poor;
        }
    }
}
