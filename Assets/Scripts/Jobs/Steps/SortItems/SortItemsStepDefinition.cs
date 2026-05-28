using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Sim.Jobs {
    [Serializable]
    public struct SortTask {
        [Tooltip("Catégorie du colis à trier. Détermine le bon bac. Assignée au colis au spawn.")]
        public SortingCategory sortingCategory;
    }

    /// <summary>
    /// Step de tri : spawne N items au point de pickup, attend que le joueur
    /// les ramasse un par un et les dépose dans le bon bac (SortingBin).
    /// L'exactitude est écrite dans le contexte pour AccuracyBasedScorer.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Steps/Sort Items", fileName = "SortItemsStep")]
    public class SortItemsStepDefinition : JobStepDefinition {
        [Tooltip("Config unique du colis spawné pour le tri (prefab Job Package). La catégorie n'est PAS lue ici — elle vient de chaque SortTask.")]
        [SerializeField] private ItemConfig packageConfig;

        [Tooltip("Liste des colis à trier (une catégorie par colis).")]
        [SerializeField] private List<SortTask> tasks = new List<SortTask>();

        [Tooltip("RoomId du ServerItemManager. POC = 'city'.")]
        [SerializeField] private string roomId = "city";

        [Tooltip("Cible où spawner les items.")]
        [SerializeField] private JobTargetKey spawnAtKey = JobTargetKey.Pickup;

        [Tooltip("Id d'un JobSpawnSlots de la scène. Si renseigné, les colis sont posés sur ses " +
                 "slots (position + rotation), dans l'ordre. Sinon, repli sur l'alignement " +
                 "linéaire au-dessus du target (baseSpawnOffset + itemSpacing).")]
        [FormerlySerializedAs("spawnShelfId")]
        [SerializeField] private string spawnSlotsId;

        [Tooltip("Si coché : répartit les colis sur des slots LIBRES DISTINCTS tirés au hasard " +
                 "parmi les slots non réservés (table d'attribution tenue par JobSpawnSlots), " +
                 "au lieu d'utiliser les slots dans l'ordre. Utile si tu renseignes plus de slots que de colis.")]
        [SerializeField] private bool randomSlot = false;

        [Tooltip("[Repli sans slots] Offset de base appliqué à la position du target pour le spawn (hauteur).")]
        [SerializeField] private Vector3 baseSpawnOffset = new Vector3(0f, 0.5f, 0f);

        [Tooltip("[Repli sans slots] Espacement horizontal entre les items spawnés.")]
        [SerializeField] private float itemSpacing = 0.6f;

        public ItemConfig PackageConfig => packageConfig;
        public IReadOnlyList<SortTask> Tasks => tasks;
        public string RoomId => roomId;
        public JobTargetKey SpawnAtKey => spawnAtKey;
        public string SpawnSlotsId => spawnSlotsId;
        public bool RandomSlot => randomSlot;
        public Vector3 BaseSpawnOffset => baseSpawnOffset;
        public float ItemSpacing => itemSpacing;

        public override JobStepInstance CreateInstance(JobInstance owner)
            => new SortItemsStepInstance(owner, this);

        public override string GetActiveTargetKey() => spawnAtKey.ToKey();

        public override MissionHighlightKind GetHighlightKinds()
            => MissionHighlightKind.Colis | MissionHighlightKind.SortingBin;
    }
}
