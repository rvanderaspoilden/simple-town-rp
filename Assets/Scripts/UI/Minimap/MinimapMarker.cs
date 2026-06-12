using System;
using System.Collections.Generic;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.UI {
    /// <summary>
    /// Drop this on an empty GameObject placed in the scene at the world position
    /// you want to mark on the minimap. The icon style (sprite, color, size) comes
    /// from the referenced <see cref="MinimapMarkerConfig"/>. The marker is bound
    /// to a single <see cref="roomId"/> and only appears when that room's map is
    /// shown by the minimap.
    ///
    /// Registers itself in a static per-room registry on enable. The minimap
    /// controller listens to <see cref="OnRegistryChanged"/> to spawn/despawn its
    /// pooled UI Images. Same pattern as <see cref="Sim.Missions.MissionPoint.ByPointId"/>.
    /// </summary>
    public class MinimapMarker : MonoBehaviour {
        [Tooltip("Visual style (sprite/color/size). Create one via Create ▸ Configurations ▸ Minimap Marker.")]
        [SerializeField] private MinimapMarkerConfig config;

        [Tooltip("Room id this marker belongs to. Must match an existing MinimapRoomMapConfig.RoomId ('city', 'hall:...', etc.).")]
        [SerializeField] private string roomId = "city";

        public MinimapMarkerConfig Config => config;
        public string              RoomId => roomId;

        private static readonly Dictionary<string, HashSet<MinimapMarker>> _byRoomId
            = new Dictionary<string, HashSet<MinimapMarker>>();
        private static readonly HashSet<MinimapMarker> _empty = new HashSet<MinimapMarker>();

        /// <summary>Fired on any registry change (marker added/removed/toggled).</summary>
        public static event Action OnRegistryChanged;

        /// <summary>Markers currently registered for the given room id (read-only snapshot).</summary>
        public static IReadOnlyCollection<MinimapMarker> GetMarkersForRoom(string roomId) {
            if (string.IsNullOrEmpty(roomId)) return _empty;
            return _byRoomId.TryGetValue(roomId, out var set) ? (IReadOnlyCollection<MinimapMarker>)set : _empty;
        }

        private void OnEnable() {
            if (string.IsNullOrEmpty(roomId)) return;
            if (!_byRoomId.TryGetValue(roomId, out var set)) {
                set = new HashSet<MinimapMarker>();
                _byRoomId[roomId] = set;
            }
            set.Add(this);
            OnRegistryChanged?.Invoke();
        }

        private void OnDisable() {
            if (string.IsNullOrEmpty(roomId)) return;
            if (_byRoomId.TryGetValue(roomId, out var set)) {
                set.Remove(this);
            }
            OnRegistryChanged?.Invoke();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            Gizmos.color = config != null ? config.Color : new Color(0.3f, 0.6f, 1f, 1f);
            Gizmos.DrawSphere(transform.position, 0.3f);
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
#endif
    }
}
