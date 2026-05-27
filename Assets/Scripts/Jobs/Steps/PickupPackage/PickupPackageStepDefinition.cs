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

        [Tooltip("Optionnel. Id d'un JobSpawnSlots (ex. palette de livraison) : si renseigné " +
                 "et trouvé, le colis est posé sur le slot 'spawnSlotIndex' (position + rotation) " +
                 "au lieu de la position du target. Vide = ancien comportement (target + offset).")]
        [SerializeField] private string spawnSlotsId;

        [Tooltip("Index du slot à utiliser dans le JobSpawnSlots ci-dessus (ignoré si Random Slot est coché).")]
        [SerializeField] private int spawnSlotIndex = 0;

        [Tooltip("Si coché : choisit automatiquement un slot LIBRE au hasard parmi tous les slots " +
                 "(vérifie qu'aucun item n'est déjà posé dessus), au lieu d'utiliser Spawn Slot Index. " +
                 "Permet de renseigner plusieurs slots et de répartir les spawns.")]
        [SerializeField] private bool randomSlot = false;

        [Tooltip("Rayon (m) de détection d'occupation d'un slot : un item posé au sol dans ce rayon " +
                 "rend le slot indisponible pour le tirage aléatoire.")]
        [SerializeField] private float slotOccupancyRadius = 0.4f;

        [Tooltip("Offset local appliqué à la position du target pour le spawn (fallback sans slots).")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0, 0.5f, 0);

        public ItemConfig ItemConfig => itemConfig;
        public string RoomId => roomId;
        public JobTargetKey SpawnAtKey => spawnAtKey;
        public string SpawnSlotsId => spawnSlotsId;
        public int SpawnSlotIndex => spawnSlotIndex;
        public bool RandomSlot => randomSlot;
        public float SlotOccupancyRadius => slotOccupancyRadius;
        public Vector3 SpawnOffset => spawnOffset;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new PickupPackageStepInstance(owner, this);

        public override string GetActiveTargetKey() => spawnAtKey.ToKey();

        public override MissionHighlightKind GetHighlightKinds() => MissionHighlightKind.Colis;
    }
}
