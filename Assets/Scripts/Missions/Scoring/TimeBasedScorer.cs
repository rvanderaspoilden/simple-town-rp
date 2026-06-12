using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Score basé sur la durée d'exécution de la mission. Plus le joueur est
    /// rapide, meilleur est le rating. Les seuils sont configurés en secondes.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Scoring/Time Based", fileName = "TimeBasedScorer")]
    public class TimeBasedScorer : MissionScoringDefinition {
        [Tooltip("Durée max (secondes) pour obtenir Perfect.")]
        [Min(1f)] [SerializeField] private float perfectSeconds = 60f;

        [Tooltip("Durée max (secondes) pour obtenir Good.")]
        [Min(1f)] [SerializeField] private float goodSeconds = 120f;

        [Tooltip("Durée max (secondes) pour obtenir Ok. Au-delà : Poor.")]
        [Min(1f)] [SerializeField] private float okSeconds = 200f;

        public override MissionRating Evaluate(MissionInstance job) {
            float t = job.ElapsedSeconds;
            if (t <= perfectSeconds) return MissionRating.Perfect;
            if (t <= goodSeconds)    return MissionRating.Good;
            if (t <= okSeconds)      return MissionRating.Ok;
            return MissionRating.Poor;
        }
    }
}
