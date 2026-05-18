using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    [Serializable]
    public struct SortTask {
        [Tooltip("Item à trier. Sa SortingCategory détermine le bon bac.")]
        public ItemConfig itemConfig;
    }

    /// <summary>
    /// Step de tri : spawne N items au point de pickup, attend que le joueur
    /// les ramasse un par un et les dépose dans le bon bac (SortingBin).
    /// L'exactitude est écrite dans le contexte pour AccuracyBasedScorer.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Steps/Sort Items", fileName = "SortItemsStep")]
    public class SortItemsStepDefinition : JobStepDefinition {
        [Tooltip("Liste des items à trier.")]
        [SerializeField] private List<SortTask> tasks = new List<SortTask>();

        [Tooltip("RoomId du ServerItemManager. POC = 'city'.")]
        [SerializeField] private string roomId = "city";

        [Tooltip("Cible où spawner les items.")]
        [SerializeField] private JobTargetKey spawnAtKey = JobTargetKey.Pickup;

        [Tooltip("Offset de base appliqué à la position du target pour le spawn (hauteur).")]
        [SerializeField] private Vector3 baseSpawnOffset = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Espacement horizontal entre les items spawnés.")]
        [SerializeField] private float itemSpacing = 0.6f;

        [Tooltip("Rayon max (mètres) dans lequel un drop est attribué à un bac.")]
        [Min(0.5f)]
        [SerializeField] private float binDropRadius = 2.5f;

        public IReadOnlyList<SortTask> Tasks => tasks;
        public string RoomId => roomId;
        public JobTargetKey SpawnAtKey => spawnAtKey;
        public Vector3 BaseSpawnOffset => baseSpawnOffset;
        public float ItemSpacing => itemSpacing;
        public float BinDropRadius => binDropRadius;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new SortItemsStepInstance(owner, this);

        public override string GetActiveTargetKey() => spawnAtKey.ToKey();
    }
}
