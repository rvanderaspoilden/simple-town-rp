using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Sim.Entities.Persistence {

    // ── Common ──────────────────────────────────────────────────────────────

    [Serializable]
    public class Vector3Body {
        public float x;
        public float y;
        public float z;

        public Vector3Body() { }
        public Vector3Body(UnityEngine.Vector3 v) { x = v.x; y = v.y; z = v.z; }
        public UnityEngine.Vector3 ToVector3() => new UnityEngine.Vector3(x, y, z);
    }

    // ── Place ───────────────────────────────────────────────────────────────

    /// <summary>Body for POST /places (idempotent on placeKey).</summary>
    [Serializable]
    public class CreatePlaceBody {
        public string placeKey;
        public string type;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ownerId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string tenantId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> properties;
    }

    [Serializable]
    public class PlaceJson {
        [JsonProperty("_id")] public string Id;
        public string placeKey;
        public string type;
        public string ownerId;
        public string tenantId;
        public Dictionary<string, object> properties;
    }

    // ── Prop ────────────────────────────────────────────────────────────────

    /// <summary>Body for POST /props — typically called at buy time.</summary>
    [Serializable]
    public class CreatePropBody {
        public string placeId;
        public int configId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Vector3Body position;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Vector3Body rotation;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? isBuilt;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? presetIndex;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> stateData;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? forSale;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? price;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ownedBy;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string builtBy;
    }

    /// <summary>Body for PATCH /props/:id. expectedVersion is mandatory.</summary>
    [Serializable]
    public class UpdatePropBody {
        public int expectedVersion;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string placeId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Vector3Body position;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Vector3Body rotation;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? isBuilt;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> stateData;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? forSale;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? price;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ownedBy;
    }

    [Serializable]
    public class PropJson {
        [JsonProperty("_id")] public string Id;
        public string placeId;
        public int configId;
        public Vector3Body position;
        public Vector3Body rotation;
        public bool isBuilt;
        public int presetIndex;
        public Dictionary<string, object> stateData;
        public bool forSale;
        public int price;
        public string ownedBy;
        public string containerPropId;
        public string builtBy;
        public int version;
    }

    // ── Transaction (POST /transactions) ────────────────────────────────────

    /// <summary>Body for POST /transactions — written by the server buy flow
    /// once payment + ownership transfer succeed.</summary>
    [Serializable]
    public class CreateTransactionBody {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string propId;

        public int configId;
        public string sellerId;
        public string buyerId;
        public int price;
        public string type;   // "sale" | "gift"
    }

    // ── Cover ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CoverJson {
        public string placeId;
        public string surfaceKind;
        public int surfaceIndex;
        public int paintConfigId;
        public float[] color;
    }

    // ── Item ────────────────────────────────────────────────────────────────

    /// <summary>Body for POST /items — never merges, always inserts a new row.</summary>
    [Serializable]
    public class CreateItemBody {
        public string placeId;
        public int    configId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? quantity;       // default 1

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> stateData;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ownedBy;
    }

    /// <summary>Body for POST /items/upsert — stack-aware add. Increments an
    /// existing matching stack (same place, configId, ownedBy, deep-equal
    /// stateData) or inserts a new row.</summary>
    [Serializable]
    public class UpsertItemBody {
        public string placeId;
        public int    configId;
        public int    quantity;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> stateData;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ownedBy;
    }

    /// <summary>Body for PATCH /items/:id. expectedVersion is mandatory.</summary>
    [Serializable]
    public class UpdateItemBody {
        public int expectedVersion;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string placeId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? quantity;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> stateData;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ownedBy;
    }

    [Serializable]
    public class ItemJson {
        [JsonProperty("_id")] public string Id;
        public string placeId;
        public int    configId;
        public int    quantity;
        public Dictionary<string, object> stateData;
        public string ownedBy;
        public int    version;
    }

    // ── Place state aggregate (GET /places/:id/state) ───────────────────────

    [Serializable]
    public class PlaceStateJson {
        public PlaceJson place;
        public PropJson[] props;
        public CoverJson[] covers;
        public ItemJson[] items;
    }
}
