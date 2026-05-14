using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Définition statique d'un objet à emballer. Forme exprimée en cellules
    /// de grille relatives. La forme doit être normalisée (min x,y == 0).
    /// </summary>
    [CreateAssetMenu(fileName = "PackageItemDefinition", menuName = "Configurations/Packaging/Item Definition")]
    public class PackageItemDefinition : ScriptableObject {
        [Header("Identification")]
        public string itemId;
        public string displayName;
        public Sprite icon;

        [Header("Shape (cells relative to (0,0), bottom-left origin)")]
        public PackageShape shape;

        [Header("Flags")]
        public bool fragile;
        public bool heavy;
        public bool allowRotation = true;

        [Header("Feedback")]
        public AudioClip placeSound;
        public Color tint = Color.white;
    }
}
