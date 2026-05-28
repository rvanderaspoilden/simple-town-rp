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

        [Tooltip("Optionnel. PointId d'un JobPoint exact (ex. 'hotel', 'foodcourt'). Si renseigné " +
                 "et trouvé dans la scène, écrase le target du JobContext pour cette mission — " +
                 "le joueur livre à CE point précis au lieu du tirage aléatoire du publisher. " +
                 "L'override s'applique aussi aux steps suivants utilisant la même TargetKey.")]
        [SerializeField] private string overrideTargetPointId;

        public float InteractionRadius => interactionRadius;
        public float HandoverSeconds => handoverSeconds;
        public JobTargetKey TargetKey => targetKey;
        public string OverrideTargetPointId => overrideTargetPointId;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new DeliverToTargetStepInstance(owner, this);

        public override string GetActiveTargetKey() => targetKey.ToKey();

        public override bool ShowsTargetBeacon => true;
    }
}
