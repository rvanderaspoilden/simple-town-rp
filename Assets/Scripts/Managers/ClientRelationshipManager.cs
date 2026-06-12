using System;
using System.Collections.Generic;
using Sim.Entities;

namespace Sim {
    /// <summary>One known relationship of the local player with another character.</summary>
    public struct RelationshipEntry {
        public RelationshipState State;
        public string FullName;
        public string JobProfessionId;   // "" = none
        public string MetAt;
        public bool Online;
    }

    /// <summary>
    /// Client-side store of the LOCAL player's relationships, keyed by the other
    /// character's id. Absence of an entry = RelationshipState.Unknown. Hydrated
    /// from the backend on connect and updated live by S2C_RelationshipUpdate.
    /// Consulted by CameraManager.ResolveHoverName (gate), the radial menu, the
    /// identity card and the Contacts phone app. Plain C# singleton.
    /// </summary>
    public class ClientRelationshipManager {
        private static ClientRelationshipManager _instance;
        public static ClientRelationshipManager Instance => _instance ??= new ClientRelationshipManager();

        private readonly Dictionary<string, RelationshipEntry> _entries = new Dictionary<string, RelationshipEntry>();

        /// <summary>Fired whenever a contact's online presence changes
        /// (S2C_ContactPresence). UIs (ContactsUI, SmsConversationUI header)
        /// subscribe to refresh in real time.</summary>
        public static event Action<string, bool> OnPresenceChanged;

        /// <summary>Fired when a relationship is removed (S2C_RelationshipRemoved).
        /// UIs (ContactsUI) subscribe to refresh.</summary>
        public static event Action<string> OnRelationshipRemoved;

        public RelationshipState GetState(string characterId) {
            if (string.IsNullOrEmpty(characterId)) return RelationshipState.Unknown;
            return _entries.TryGetValue(characterId, out RelationshipEntry e) ? e.State : RelationshipState.Unknown;
        }

        public bool TryGet(string characterId, out RelationshipEntry entry) {
            if (!string.IsNullOrEmpty(characterId)) return _entries.TryGetValue(characterId, out entry);
            entry = default;
            return false;
        }

        public IReadOnlyDictionary<string, RelationshipEntry> All => _entries;

        public void Set(string characterId, RelationshipState state, string fullName, string jobProfessionId, string metAt, bool online) {
            if (string.IsNullOrEmpty(characterId)) return;
            // Preserve previously-known fields when an update omits them.
            _entries.TryGetValue(characterId, out RelationshipEntry prev);
            // Son de poignée de main quand on devient connaissance (transition vers >= Acquaintance).
            if (prev.State < RelationshipState.Acquaintance && state >= RelationshipState.Acquaintance)
                Sim.Audio.AudioManager.Instance.PlayUI(Sim.Audio.SfxId.Handshake);
            _entries[characterId] = new RelationshipEntry {
                State = state,
                FullName = !string.IsNullOrEmpty(fullName) ? fullName : prev.FullName,
                JobProfessionId = !string.IsNullOrEmpty(jobProfessionId) ? jobProfessionId : prev.JobProfessionId,
                MetAt = !string.IsNullOrEmpty(metAt) ? metAt : prev.MetAt,
                Online = online,
            };
        }

        public void SetOnline(string characterId, bool online) {
            if (string.IsNullOrEmpty(characterId) || !_entries.TryGetValue(characterId, out RelationshipEntry prev)) return;
            if (prev.Online == online) return;
            prev.Online = online;
            _entries[characterId] = prev;
            OnPresenceChanged?.Invoke(characterId, online);
        }

        public void Remove(string characterId) {
            if (string.IsNullOrEmpty(characterId)) return;
            if (_entries.Remove(characterId)) OnRelationshipRemoved?.Invoke(characterId);
        }

        public void Hydrate(IEnumerable<RelationshipData> relationships) {
            _entries.Clear();
            if (relationships == null) return;
            foreach (RelationshipData r in relationships) {
                if (string.IsNullOrEmpty(r?.otherCharacterId)) continue;
                _entries[r.otherCharacterId] = new RelationshipEntry {
                    State = r.State,
                    FullName = r.otherFullName,
                    JobProfessionId = r.jobProfessionId,
                    MetAt = r.metAt,
                    Online = r.online,
                };
            }
        }

        public void Clear() => _entries.Clear();
    }
}
