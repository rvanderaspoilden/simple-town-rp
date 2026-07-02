using Sim.Entities;
using Sim.Enums;
using UnityEngine;

namespace Sim.Building {
    /// <summary>
    /// Contract shared by every space a player can build furniture into. Lets the build pipeline
    /// (BuildManager client-side, PropInteractionDispatcher server-side) operate on apartments and
    /// land parcels without knowing which it is.
    ///
    /// Two implementations:
    ///   - <see cref="ApartmentController"/> — walled apartment, props persisted to the home place,
    ///     props visible in the hall room.
    ///   - <see cref="LandParcelController"/> — open outdoor lot, no walls, props persisted to a
    ///     dedicated land place but kept in the shared "city" room.
    /// </summary>
    public interface IBuildableSpace {
        /// <summary>True if <paramref name="character"/> is allowed to build here
        /// (apartment: is the tenant ; land: is the owner).</summary>
        bool CanBuild(CharacterData character);

        /// <summary>Prop-sync room id (visibility scope). Apartment: the hall room ; land: "city".</summary>
        string RoomId { get; }

        /// <summary>Persistence place id (DB). Apartment: HomeData.Id ; land: the land place uuid.
        /// Decoupled from <see cref="RoomId"/> on purpose — land props live in "city" but persist
        /// to their own place.</summary>
        string PlaceId { get; }

        /// <summary>Character id of the owner used as the prop's ownerCharId (apartment: tenant).</summary>
        string OwnerCharId { get; }

        Transform PropsContainer { get; }

        /// <summary>False for spaces with no walls (land) — the build UI skips wall visibility wiring.</summary>
        bool HasWalls { get; }

        void TrackProp(int propId);
        void UntrackProp(int propId);
        bool OwnsProp(int propId);

        /// <summary>No-op for spaces without walls (land).</summary>
        void SetWallVisibility(VisibilityModeEnum mode);
    }
}
