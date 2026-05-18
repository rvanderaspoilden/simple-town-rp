using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Step de remise. Le joueur doit rester à proximité de la cible pendant
    /// handoverSeconds ; sortir du rayon réinitialise le compteur (pas d'échec
    /// brutal — UX wholesome). La cible qui disparaît fait échouer le step.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Steps/Deliver To Target", fileName = "DeliverToTargetStep")]
    public class DeliverToTargetStepDefinition : JobStepDefinition {
        [Tooltip("Distance maximale entre le owner et la cible pour valider la remise.")]
        [Min(0.1f)]
        [SerializeField] private float interactionRadius = 1.75f;

        [Tooltip("Durée pendant laquelle le owner doit rester à portée (secondes).")]
        [Min(0f)]
        [SerializeField] private float handoverSeconds = 1.2f;

        [Tooltip("Slot de cible à viser dans le JobContext.")]
        [SerializeField] private JobTargetKey targetKey = JobTargetKey.Delivery;

        public float InteractionRadius => interactionRadius;
        public float HandoverSeconds => handoverSeconds;
        public JobTargetKey TargetKey => targetKey;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new DeliverToTargetStepInstance(owner, this);

        public override string GetActiveTargetKey() => targetKey.ToKey();
    }
}
