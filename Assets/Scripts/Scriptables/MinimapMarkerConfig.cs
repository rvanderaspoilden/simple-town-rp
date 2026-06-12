using UnityEngine;

namespace Sim.Scriptables {
    /// <summary>
    /// Visual style for a minimap POI marker (passive icon shown at a world
    /// location on the minimap). Reusable across many markers — drop the same
    /// config on every "shop entry", "warehouse dock", etc. Picked up by the
    /// <c>MinimapMarker</c> component placed on empty GameObjects in the scene.
    /// </summary>
    [CreateAssetMenu(fileName = "New Minimap Marker", menuName = "Configurations/Minimap Marker")]
    public class MinimapMarkerConfig : ScriptableObject {
        [Tooltip("Sprite displayed on the minimap at the marker's world position.")]
        [SerializeField] private Sprite sprite;

        [Tooltip("Color tint applied to the sprite.")]
        [SerializeField] private Color color = Color.white;

        [Tooltip("Width/height (in UI pixels) of the marker icon on the minimap.")]
        [SerializeField] private float pixelSize = 28f;

        public Sprite Sprite    => sprite;
        public Color  Color     => color;
        public float  PixelSize => pixelSize;
    }
}
