using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Sim.Entities.Persistence {

    // ── Constellation (GET / POST /characters/:id/constellation/*) ───────────
    //
    // The backend persists per-character `points` (single unified wallet keyed by
    // BranchConfig.id — branches racines ET sous-branches confondues) + the set of
    // unlocked node ids. The graph definition (cost, prereqs, layout) stays
    // client-authored in ConstellationGraphConfig ; the server only validates the
    // wallet on unlock.

    /// <summary>One currency wallet entry. Serialized as an array element in the
    /// wrapped response because JsonUtility cannot deserialize arbitrary
    /// dictionaries. `id` = BranchConfig.id.</summary>
    [Serializable]
    public class PointsEntry {
        public string id;
        public int points;
    }

    /// <summary>Full state of a character's constellation, as returned by every
    /// endpoint (GET / unlock / grant). `points` is a sparse array — only
    /// currencies the player has ever touched come back.</summary>
    [Serializable]
    public class ConstellationStateData {
        public string character_id;
        public PointsEntry[] points;
        public string[] unlocked_node_ids;
        public string last_discovered_node_id;
        public string updated_at;
    }

    /// <summary>Wrapped response for GET / POST endpoints. Mirrors the
    /// CharacterResponse / HomeResponse pattern (Unity JsonUtility can't
    /// deserialize bare arrays).</summary>
    [Serializable]
    public class ConstellationStateResponse {
        public ConstellationStateData[] States;
    }

    // ── Request bodies ───────────────────────────────────────────────────────

    /// <summary>Body of POST /characters/:id/constellation/unlock. The client
    /// declares the node id and a FLAT cost map (key = BranchConfig.id → amount)
    /// computed locally from the graph definition. The server validates wallet only.</summary>
    public class UnlockNodeBody {
        public string nodeId;
        public Dictionary<string, int> costs;
    }

    /// <summary>Body of POST /characters/:id/constellation/grant. Single FLAT map
    /// (key = BranchConfig.id → amount). Used by the mission reward pipeline
    /// (server-side) and by the debug keys.</summary>
    public class GrantPointsBody {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, int> points;
    }
}
