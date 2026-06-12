using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Step de remise. Le joueur doit rester à proximité de la cible pendant
    /// handoverSeconds ; sortir du rayon réinitialise le compteur (pas d'échec
    /// brutal — UX wholesome). La cible qui disparaît fait échouer le step.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Steps/Deliver To Target", fileName = "DeliverToTargetStep")]
    public class DeliverToTargetStepDefinition : MissionStepDefinition {
        [Tooltip("Distance maximale entre le owner et la cible pour valider la remise.")]
        [Min(0.1f)]
        [SerializeField] private float interactionRadius = 1.75f;

        [Tooltip("Durée pendant laquelle le owner doit rester à portée (secondes).")]
        [Min(0f)]
        [SerializeField] private float handoverSeconds = 1.2f;

        [Tooltip("Slot de cible à viser dans le MissionContext.")]
        [SerializeField] private MissionTargetKey targetKey = MissionTargetKey.Delivery;

        [Tooltip("Optionnel. PointId d'un MissionPoint exact (ex. 'hotel', 'foodcourt'). Si renseigné " +
                 "et trouvé dans la scène, écrase le target du MissionContext pour cette mission — " +
                 "le joueur livre à CE point précis au lieu du tirage aléatoire du publisher. " +
                 "L'override s'applique aussi aux steps suivants utilisant la même TargetKey.")]
        [SerializeField] private string overrideTargetPointId;

        public float InteractionRadius => interactionRadius;
        public float HandoverSeconds => handoverSeconds;
        public MissionTargetKey TargetKey => targetKey;
        public string OverrideTargetPointId => overrideTargetPointId;

        public override MissionStepInstance CreateInstance(MissionInstance owner)
            => new DeliverToTargetStepInstance(owner, this);

        public override string GetActiveTargetKey() => targetKey.ToKey();

        public override bool ShowsTargetBeacon => true;
    }
}
