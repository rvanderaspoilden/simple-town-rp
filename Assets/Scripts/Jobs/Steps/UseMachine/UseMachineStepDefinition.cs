using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Step : attend que le joueur interagisse avec une machine en monde
    /// (PackagingMachineBehaviour). À l'interaction, l'item-colis spawn
    /// directement dans les mains du joueur via ServerItemManager.SpawnItemInHand.
    ///
    /// Pas de spawn automatique au sol — contrairement à PickupPackageStep.
    /// Le minigame d'emballage viendra se brancher entre l'interaction et le
    /// spawn (à venir).
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Steps/Use Machine", fileName = "UseMachineStep")]
    public class UseMachineStepDefinition : JobStepDefinition {
        [Tooltip("ItemConfig du colis à spawner dans les mains du joueur.")]
        [SerializeField] private ItemConfig itemConfig;

        [Tooltip("RoomId du ServerItemManager. POC = 'city'.")]
        [SerializeField] private string roomId = "city";

        [Tooltip("Cible géographique où la machine est censée se trouver (pour l'indicateur HUD).")]
        [SerializeField] private JobTargetKey aimTargetKey = JobTargetKey.Pickup;

        public ItemConfig ItemConfig => itemConfig;
        public string RoomId => roomId;
        public JobTargetKey AimTargetKey => aimTargetKey;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new UseMachineStepInstance(owner, this);

        public override string GetActiveTargetKey() => aimTargetKey.ToKey();
    }
}
