using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Score basé sur la durée d'exécution de la mission. Plus le joueur est
    /// rapide, meilleur est le rating. Les seuils sont configurés en secondes.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Scoring/Time Based", fileName = "TimeBasedScorer")]
    public class TimeBasedScorer : JobScoringDefinition {
        [Tooltip("Durée max (secondes) pour obtenir Perfect.")]
        [Min(1f)] [SerializeField] private float perfectSeconds = 60f;

        [Tooltip("Durée max (secondes) pour obtenir Good.")]
        [Min(1f)] [SerializeField] private float goodSeconds = 120f;

        [Tooltip("Durée max (secondes) pour obtenir Ok. Au-delà : Poor.")]
        [Min(1f)] [SerializeField] private float okSeconds = 200f;

        public override JobRating Evaluate(JobInstance job) {
            float t = job.ElapsedSeconds;
            if (t <= perfectSeconds) return JobRating.Perfect;
            if (t <= goodSeconds)    return JobRating.Good;
            if (t <= okSeconds)      return JobRating.Ok;
            return JobRating.Poor;
        }
    }
}
