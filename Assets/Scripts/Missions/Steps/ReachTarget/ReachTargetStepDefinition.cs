using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Step : "aller jusqu'à une cible du MissionContext". Vérifie périodiquement
    /// la distance entre le joueur (owner) et la cible désignée, et succeed
    /// dès que arrivalRadius est atteint. Réutilisable par tous les métiers.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Steps/Reach Target", fileName = "ReachTargetStep")]
    public class ReachTargetStepDefinition : MissionStepDefinition {
        [Tooltip("Distance d'arrivée (mètres).")]
        [Min(0.1f)]
        [SerializeField] private float arrivalRadius = 2f;

        [Tooltip("Slot de cible à viser dans le MissionContext.")]
        [SerializeField] private MissionTargetKey targetKey = MissionTargetKey.Pickup;

        [Tooltip("Optionnel. PointId d'un MissionPoint exact (ex. 'warehouse', 'hotel'). Si renseigné " +
                 "et trouvé dans la scène, écrase le target du MissionContext pour cette mission — " +
                 "le joueur est envoyé vers CE point précis au lieu du tirage aléatoire du publisher. " +
                 "L'override s'applique aussi aux steps suivants utilisant la même TargetKey.")]
        [SerializeField] private string overrideTargetPointId;

        public float ArrivalRadius => arrivalRadius;
        public MissionTargetKey TargetKey => targetKey;
        public string OverrideTargetPointId => overrideTargetPointId;

        public override MissionStepInstance CreateInstance(MissionInstance owner)
            => new ReachTargetStepInstance(owner, this);

        public override string GetActiveTargetKey() => targetKey.ToKey();

        public override bool ShowsTargetBeacon => true;
    }
}
