using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Step : spawne un item-colis à la position de la cible désignée (Pickup
    /// par défaut) via ServerItemManager, attend que le joueur le ramasse
    /// (action PICK native du système Items → mise en main automatique).
    ///
    /// Le colis reste dans les mains du joueur pendant les steps suivants ;
    /// il sera despawn au DeliverToTargetStep ou par JobItemCleanup si la
    /// mission échoue/est abandonnée.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Steps/Pickup Package", fileName = "PickupPackageStep")]
    public class PickupPackageStepDefinition : JobStepDefinition {
        [Tooltip("ItemConfig du colis à spawner au sol.")]
        [SerializeField] private ItemConfig itemConfig;

        [Tooltip("RoomId du ServerItemManager. POC = 'city'.")]
        [SerializeField] private string roomId = "city";

        [Tooltip("Cible où spawner le colis (généralement Pickup).")]
        [SerializeField] private JobTargetKey spawnAtKey = JobTargetKey.Pickup;

        [Tooltip("Offset local appliqué à la position du target pour le spawn.")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0, 0.5f, 0);

        public ItemConfig ItemConfig => itemConfig;
        public string RoomId => roomId;
        public JobTargetKey SpawnAtKey => spawnAtKey;
        public Vector3 SpawnOffset => spawnOffset;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new PickupPackageStepInstance(owner, this);

        public override string GetActiveTargetKey() => spawnAtKey.ToKey();
    }
}
