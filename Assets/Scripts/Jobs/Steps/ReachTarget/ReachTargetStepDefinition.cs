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

        [Tooltip("Quelle cible du JobContext viser : 'primary' (défaut) ou 'secondary'.")]
        [SerializeField] private string targetKey = "primary";

        public float ArrivalRadius => arrivalRadius;
        public string TargetKey => targetKey;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new ReachTargetStepInstance(owner, this);
    }
}
