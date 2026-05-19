using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Config du mini-jeu d'emballage. Étend SubGameConfiguration pour pouvoir
    /// être passé à SubGameController.LaunchSubGame.
    ///
    /// La commande à emballer n'est plus pré-définie : un PackageOrder est
    /// généré à la volée par PackageOrderGenerator depuis le 'catalog' à
    /// chaque lancement. Les decoys (leurres) viennent aussi du catalog —
    /// placer un decoy pénalise le score.
    /// </summary>
    [CreateAssetMenu(fileName = "PackagingSubGameConfig", menuName = "Configurations/Packaging/Sub Game Config")]
    public class PackagingSubGameConfig : SubGameConfiguration {
        [Header("Box")]
        [Range(3, 6)] public int gridWidth = 5;
        [Range(3, 6)] public int gridHeight = 5;

        [Header("Order generation")]
        [Tooltip("Items disponibles pour la génération de la commande et des leurres. Mixer des heavy / fragile / normaux pour que le générateur puisse remplir la grille à 100%.")]
        public PackageItemDefinition[] catalog;

        [Tooltip("Nombre d'items leurres à ajouter au tray en plus des items requis. Les placer pénalise le score.")]
        [Min(0)] public int decoyCount = 3;

        [Tooltip("Nom du client affiché en haut de la commande (purement cosmétique).")]
        public string customerName = "Client";

        [Header("Scoring weights (will be normalized)")]
        [Range(0f, 1f)] public float spaceWeight = 0.5f;
        [Range(0f, 1f)] public float fragileWeight = 0.25f;
        [Range(0f, 1f)] public float heavyWeight = 0.25f;

        [Tooltip("Pénalité appliquée pour chaque cellule de decoy placée dans la grille (en fraction du score total). 0.05 = -5% par cellule de leurre.")]
        [Range(0f, 1f)] public float decoyPenaltyPerCell = 0.05f;
    }
}
