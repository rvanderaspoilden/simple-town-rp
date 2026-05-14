using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Config du mini-jeu d'emballage. Étend SubGameConfiguration pour pouvoir
    /// être passé à SubGameController.LaunchSubGame.
    /// </summary>
    [CreateAssetMenu(fileName = "PackagingSubGameConfig", menuName = "Configurations/Packaging/Sub Game Config")]
    public class PackagingSubGameConfig : SubGameConfiguration {
        [Header("Box")]
        [Range(3, 6)] public int gridWidth = 5;
        [Range(3, 6)] public int gridHeight = 5;

        [Header("Order")]
        public PackageOrderDefinition order;

        [Header("Scoring weights (will be normalized)")]
        [Range(0f, 1f)] public float spaceWeight = 0.5f;
        [Range(0f, 1f)] public float fragileWeight = 0.25f;
        [Range(0f, 1f)] public float heavyWeight = 0.25f;
    }
}
