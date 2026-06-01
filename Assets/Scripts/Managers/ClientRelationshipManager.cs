using System.Collections.Generic;
using Sim.Entities;

namespace Sim {
    /// <summary>
    /// Client-side store of the LOCAL player's relationships, keyed by the other
    /// character's id. Absence of an entry = RelationshipState.Unknown. Hydrated
    /// from the backend on connect and updated live by S2C_RelationshipUpdate.
    /// Consulted by CameraManager.ResolveHoverName to gate identity reveal.
    /// Plain C# singleton (no MonoBehaviour), like PlayerRoomTracker.
    /// </summary>
    public class ClientRelationshipManager {
        private static ClientRelationshipManager _instance;
        public static ClientRelationshipManager Instance => _instance ??= new ClientRelationshipManager();

        private readonly Dictionary<string, RelationshipState> _states = new Dictionary<string, RelationshipState>();

        public RelationshipState GetState(string characterId) {
            if (string.IsNullOrEmpty(characterId)) return RelationshipState.Unknown;
            return _states.TryGetValue(characterId, out RelationshipState s) ? s : RelationshipState.Unknown;
        }

        public void Set(string characterId, RelationshipState state) {
            if (string.IsNullOrEmpty(characterId)) return;
            _states[characterId] = state;
        }

        public void Hydrate(IEnumerable<RelationshipData> relationships) {
            _states.Clear();
            if (relationships == null) return;
            foreach (RelationshipData r in relationships) {
                if (!string.IsNullOrEmpty(r?.otherCharacterId)) _states[r.otherCharacterId] = r.State;
            }
        }

        public void Clear() => _states.Clear();
    }
}
