using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Step : "aller jusqu'à une cible du JobContext". Vérifie périodiquement
    /// la distance entre le joueur (owner) et la cible désignée, et succeed
    /// dès que arrivalRadius est atteint. Réutilisable par tous les métiers.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Steps/Reach Target", fileName = "ReachTargetStep")]
    public class ReachTargetStepDefinition : JobStepDefinition {
        [Tooltip("Distance d'arrivée (mètres).")]
        [Min(0.1f)]
        [SerializeField] private float arrivalRadius = 2f;

        [Tooltip("Slot de cible à viser dans le JobContext.")]
        [SerializeField] private JobTargetKey targetKey = JobTargetKey.Pickup;

        public float ArrivalRadius => arrivalRadius;
        public JobTargetKey TargetKey => targetKey;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new ReachTargetStepInstance(owner, this);

        public override string GetActiveTargetKey() => targetKey.ToKey();
    }
}
