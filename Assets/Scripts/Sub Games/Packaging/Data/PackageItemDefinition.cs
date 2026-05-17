using System.Text;
using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Définition statique d'un objet à emballer. Forme exprimée en cellules
    /// de grille relatives. La forme doit être normalisée (min x,y == 0).
    ///
    /// SHAPE EXAMPLES:
    ///   1×1 block  : cells size=1  → (0,0)
    ///   1×2 tall   : cells size=2  → (0,0) (0,1)
    ///   2×1 wide   : cells size=2  → (0,0) (1,0)
    ///   2×2 block  : cells size=4  → (0,0) (1,0) (0,1) (1,1)
    ///   L-shape    : cells size=3  → (0,0) (1,0) (0,1)
    /// Every cell the item physically occupies must be listed — NOT just corners.
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
        public Color bgColor = new Color(1f, 1f, 1f, 0.35f);

#if UNITY_EDITOR
        private void OnValidate() {
            if (shape.cells == null || shape.cells.Length == 0) {
                Debug.LogWarning($"[PackageItemDefinition] '{name}': shape has no cells — item cannot be placed. Add at least (0,0).");
                return;
            }

            // Print the shape as an ASCII grid so it's easy to spot gaps.
            var bounds = shape.Bounds();
            var sb = new StringBuilder();
            sb.AppendLine($"[PackageItemDefinition] '{name}' shape ({bounds.width}×{bounds.height}, {shape.cells.Length} cells):");
            for (int y = bounds.height - 1; y >= 0; y--) {
                for (int x = 0; x < bounds.width; x++) {
                    bool filled = false;
                    foreach (var c in shape.cells) if (c.x == x && c.y == y) { filled = true; break; }
                    sb.Append(filled ? "[X]" : "[ ]");
                }
                sb.AppendLine();
            }
            Debug.Log(sb.ToString());
        }
#endif
    }
}
