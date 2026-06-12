using UnityEngine;

namespace Sim.Scriptables {
    /// <summary>
    /// Pre-drawn top-down map for a single room id, with the world↔pixel
    /// calibration the minimap needs to position the player marker correctly.
    /// Discovered at startup by <see cref="DatabaseManager"/> from
    /// <c>Resources/Configurations/Minimap</c>.
    ///
    /// If no asset matches the local room id, the minimap hides itself.
    /// </summary>
    [CreateAssetMenu(fileName = "New Minimap Room Map", menuName = "Configurations/Minimap Room Map")]
    public class MinimapRoomMapConfig : ScriptableObject {
        [Tooltip("Room id this map applies to ('city', 'hall:...', 'apartment:...', etc.).")]
        [SerializeField] private string roomId;

        [Tooltip("Pre-drawn top-down map sprite for this room.")]
        [SerializeField] private Sprite sprite;

        [Tooltip("World X at the center of the captured map.")]
        [SerializeField] private float worldCenterX;

        [Tooltip("World Z at the center of the captured map.")]
        [SerializeField] private float worldCenterZ;

        [Tooltip("Half-extent of the captured square in world units (= camera orthographic size at capture).")]
        [SerializeField] private float worldHalfSize = 50f;

        [Tooltip("Pixel side of the MapImage RectTransform representing the full world square (2*worldHalfSize).")]
        [SerializeField] private float mapImagePixelSize = 1024f;

        public string RoomId            => roomId;
        public Sprite Sprite            => sprite;
        public float  WorldCenterX      => worldCenterX;
        public float  WorldCenterZ      => worldCenterZ;
        public float  WorldHalfSize     => worldHalfSize;
        public float  MapImagePixelSize => mapImagePixelSize;
    }
}
