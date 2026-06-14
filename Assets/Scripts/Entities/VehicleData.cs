using System;

namespace Sim.Entities {
    /// <summary>
    /// Persisted vehicle ownership record (backend table `vehicles`). Matched to an in-world
    /// vehicle by <see cref="vehicleKey"/> (a stable scene key). Position is NOT persisted.
    /// (De)serialized with JsonUtility — keep public fields matching the backend JSON keys.
    /// </summary>
    [Serializable]
    public class VehicleData {
        public string id;
        public string vehicleKey;       // "" for a purchased/garaged vehicle
        public string ownerCharacterId; // "" = unowned
        public string modelId;          // VehicleConfig.id (display name resolved from the config)
        public string placeId;          // "" for a world vehicle; garage place for a purchased one
    }

    /// <summary>Body for POST /vehicles (Unity server, on purchase at the dealership).</summary>
    [Serializable]
    public class CreateGaragedVehicleBody {
        public string ownerCharacterId;
        public string modelId;
        public string placeId;
    }

    /// <summary>Wrapper for GET /characters/:id/vehicles (JsonUtility needs a root object).</summary>
    [Serializable]
    public class VehicleListResponse {
        public System.Collections.Generic.List<VehicleData> vehicles = new System.Collections.Generic.List<VehicleData>();
    }

    /// <summary>Body for POST /vehicles/ensure (Unity server, on spawn).</summary>
    [Serializable]
    public class EnsureVehicleBody {
        public string vehicleKey;
        public string modelId;
    }

    /// <summary>Body for PATCH /vehicles/:key/owner (Unity server, on claim/transfer).</summary>
    [Serializable]
    public class SetVehicleOwnerBody {
        public string ownerCharacterId;
    }
}
