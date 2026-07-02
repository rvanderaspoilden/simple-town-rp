using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Entities;
using Sim.Entities.Persistence;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim.Building {
    /// <summary>
    /// An outdoor land parcel a player can buy and build furniture on. Lightweight counterpart to
    /// <see cref="ApartmentController"/>: no walls, no covers, no preset, no hall — just open ground.
    ///
    /// Implements <see cref="IBuildableSpace"/> so the shared build pipeline (BuildManager client-side,
    /// PropInteractionDispatcher server-side) treats it like an apartment. Key difference: its props
    /// live in the shared <c>"city"</c> room (visible to everyone, no EnterRoom on a trigger) but
    /// persist to a dedicated place <c>land:{id}</c>.
    ///
    /// Ownership + persistence are driven by <see cref="LandSystemBootstrap"/>: it sets ownership on
    /// purchase and restores it on server start. Build messages are routed to the right parcel via
    /// the static <see cref="ById"/> registry keyed by <see cref="LandConfig.id"/>.
    /// </summary>
    public class LandParcelController : MonoBehaviour, IBuildableSpace {
        [Header("Settings")]
        [SerializeField, Tooltip("Land config (id, price, surface). The persistence place is land:{id}.")]
        private LandConfig config;

        [SerializeField, Tooltip("Parent transform that owns this parcel's built props (server-side hierarchy).")]
        private Transform propsContainer;

        [Header("Delivery box")]
        [SerializeField, Tooltip("PropsConfig of the delivery box prefab. Spawned for the owner so they can enter build mode.")]
        private PropsConfig deliveryBoxPrefabConfig;

        [SerializeField, Tooltip("Where to spawn the owner's delivery box on this parcel.")]
        private Transform deliveryBoxSpawn;

        // Runtime occupancy state (mirrored on clients via LandSystemBootstrap S2C messages).
        // A parcel is either OWNED (bought, permanent) or RENTED (recurring rent), never both.
        private string ownerCharId;
        private string ownerName;
        private string tenantCharId;
        private string tenantName;
        private string landPlaceId;
        private int    deliveryBoxPropId;

        // The single character who occupies the parcel right now (owner if bought, else tenant
        // if rented). Their character id tags the delivery box, the persisted props, and build rights.
        private string OccupantCharId => !string.IsNullOrEmpty(ownerCharId) ? ownerCharId : tenantCharId;

        // Tracks every prop this parcel spawned so OnDestroy can clean up only its own props
        // (the "city" room is shared with the rest of the world).
        private readonly HashSet<int> _ownedPropIds = new HashSet<int>();

        /// <summary>Server+client registry of parcels keyed by LandConfig.id. Build messages carry a
        /// parcelId because the "city" room can't be routed by connection like apartments.</summary>
        public static readonly Dictionary<int, LandParcelController> ById = new Dictionary<int, LandParcelController>();

        /// <summary>Fired on the client when this parcel's occupancy (owner or tenant) changes,
        /// so the For-Sale sign and info modal refresh.</summary>
        public event System.Action OnClientStateChanged;

        private void Awake() {
            if (config != null) {
                if (ById.TryGetValue(config.id, out var existing) && existing != null && existing != this)
                    Debug.LogWarning($"[LandParcel] Duplicate parcel id {config.id} — overwriting registry entry.");
                ById[config.id] = this;
            } else {
                Debug.LogWarning($"[LandParcel] {name} has no LandConfig assigned — it cannot be bought or built on.");
            }
        }

        private void Start() {
            // Server-driven boot restore. OnServerStart fires before the City scene finishes
            // loading on a host, so a central bootstrap can't see this parcel yet — each parcel
            // restores its own ownership here instead (mirrors ApartmentController's self-load).
            if (NetworkServer.active && config != null && ApiManager.Instance != null)
                StartCoroutine(ServerRestoreOwnership());
        }

        /// <summary>Looks up this parcel's <c>land:{id}</c> place WITHOUT creating it. If it exists
        /// and is occupied, resolve the (possibly offline) occupant's display name and apply the
        /// matching occupancy — owned (owner_id) or rented (tenant_id) — which spawns the delivery
        /// box and reloads persisted props.</summary>
        [Server]
        private IEnumerator ServerRestoreOwnership() {
            PlaceJson place = null;
            yield return ApiManager.Instance.LookupPlaceByKeyCoroutine(config.PlaceKey, p => place = p);

            if (place == null) yield break; // never claimed → on sale
            bool owned  = !string.IsNullOrEmpty(place.ownerId);
            bool rented = !owned && !string.IsNullOrEmpty(place.tenantId);
            if (!owned && !rented) yield break; // place exists but vacant → on sale/rent

            string occupantId = owned ? place.ownerId : place.tenantId;
            string occupantName = null;
            UnityWebRequest req = ApiManager.Instance.RetrieveCharacterByIdRequest(occupantId);
            yield return req.SendWebRequest();
            if (req.responseCode == 200) {
                CharacterResponse resp = JsonUtility.FromJson<CharacterResponse>(req.downloadHandler.text);
                if (resp?.Characters != null && resp.Characters.Length > 0)
                    occupantName = resp.Characters[0].Identity.FullName;
            }

            if (owned) ServerSetOwnership(occupantId, occupantName, place.Id);
            else       ServerSetTenancy(occupantId, occupantName, place.Id);
        }

        private void OnDestroy() {
            if (config != null && ById.TryGetValue(config.id, out var c) && c == this)
                ById.Remove(config.id);

            if (NetworkServer.active) {
                foreach (int propId in _ownedPropIds.ToArray())
                    ServerPropManager.Instance?.RemoveProp(this.RoomId, propId);
                _ownedPropIds.Clear();
                this.deliveryBoxPropId = 0;
            }
        }

        // ── Public surface ────────────────────────────────────────────────────

        public LandConfig Config => config;
        public int ConfigId => config != null ? config.id : 0;
        public string OwnerName => ownerName;
        public bool IsOwned => !string.IsNullOrEmpty(ownerCharId);
        public string TenantName => tenantName;
        public string TenantCharId => tenantCharId;
        public bool IsRented => !IsOwned && !string.IsNullOrEmpty(tenantCharId);

        // ── Server ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Server entry point used by <see cref="LandSystemBootstrap"/> for both purchase and
        /// boot-time restore. Sets ownership (clearing any tenancy — a parcel is owned XOR rented),
        /// spawns the owner's delivery box (idempotent), and loads any persisted props.
        /// </summary>
        [Server]
        public void ServerSetOwnership(string ownerCharacterId, string ownerDisplayName, string placeId) {
            this.ownerCharId  = ownerCharacterId;
            this.ownerName    = ownerDisplayName;
            this.tenantCharId = null;
            this.tenantName   = null;
            this.landPlaceId  = placeId;

            SpawnDeliveryBox();
            if (!string.IsNullOrEmpty(placeId))
                StartCoroutine(LoadPlaceState(placeId));
        }

        /// <summary>
        /// Server entry point for renting (<see cref="LandSystemBootstrap"/>) and boot-time restore
        /// of a rented parcel. Same runtime behaviour as ownership — the tenant gets a delivery box
        /// and build rights — but the parcel reads as rented (recurring rent) rather than owned.
        /// </summary>
        [Server]
        public void ServerSetTenancy(string tenantCharacterId, string tenantDisplayName, string placeId) {
            this.tenantCharId = tenantCharacterId;
            this.tenantName   = tenantDisplayName;
            this.ownerCharId  = null;
            this.ownerName    = null;
            this.landPlaceId  = placeId;

            SpawnDeliveryBox();
            if (!string.IsNullOrEmpty(placeId))
                StartCoroutine(LoadPlaceState(placeId));
        }

        /// <summary>
        /// Server side of a sale / lease termination / eviction (<see cref="LandSystemBootstrap"/>):
        /// tears down runtime occupancy after the backend has already moved the built props to a
        /// transit place and deleted the land place row. Despawns every prop this parcel owns
        /// (furniture + delivery box) WITHOUT touching the DB — the props are no longer attached to
        /// this parcel persistence-side — then resets runtime state so the parcel reads as on
        /// sale / for rent again.
        /// </summary>
        [Server]
        public void ServerVacate() {
            foreach (int propId in _ownedPropIds.ToArray()) {
                ServerPropManager.Instance?.RemoveProp(this.RoomId, propId); // runtime despawn only
                ServerPropManager.Instance?.ClearBridge(propId);             // drop stale int→UUID map
            }
            _ownedPropIds.Clear();

            this.ownerCharId       = null;
            this.ownerName         = null;
            this.tenantCharId      = null;
            this.tenantName        = null;
            this.landPlaceId       = null;
            this.deliveryBoxPropId = 0;
        }

        [Server]
        private void SpawnDeliveryBox() {
            if (this.deliveryBoxPropId > 0) return;                 // idempotent
            if (this.deliveryBoxPrefabConfig == null || this.deliveryBoxSpawn == null) {
                Debug.LogWarning($"[LandParcel] {name}: no delivery box config/spawn — owner won't be able to build.");
                return;
            }

            int boxId = ServerPropManager.Instance.SpawnProp(
                this.RoomId,
                this.deliveryBoxPrefabConfig.GetId(),
                this.deliveryBoxSpawn.position,
                this.deliveryBoxSpawn.rotation,
                ownerCharId: this.OccupantCharId
            );
            if (boxId < 0) return;
            TrackProp(boxId);
            this.deliveryBoxPropId = boxId;

            GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(boxId);
            if (go != null && this.propsContainer != null) {
                go.transform.SetParent(this.propsContainer);
                go.transform.position = this.deliveryBoxSpawn.position;
                go.transform.rotation = this.deliveryBoxSpawn.rotation;
            }

            // Reflect the occupant's pending deliveries on the box (same UX as an apartment).
            if (!string.IsNullOrEmpty(this.OccupantCharId))
                PropInteractionDispatcher.Instance?.RefreshDeliveryBoxCount(boxId, this.RoomId, this.OccupantCharId);
        }

        /// <summary>
        /// Props-only hydration from GET /places/:id/state. Mirrors
        /// <see cref="ApartmentController.InstantiateLevelFromPlaceState"/> MINUS walls / covers /
        /// preset / doors — a land parcel only carries player-placed furniture.
        /// </summary>
        [Server]
        private IEnumerator LoadPlaceState(string placeId) {
            UnityWebRequest req = ApiManager.Instance.GetPlaceStateRequest(placeId);
            yield return req.SendWebRequest();

            if (req.responseCode != 200) {
                Debug.LogWarning($"[LandParcel] /places/{placeId}/state failed ({req.responseCode}) — no props restored.");
                yield break;
            }

            PlaceStateJson state;
            try {
                state = Newtonsoft.Json.JsonConvert.DeserializeObject<PlaceStateJson>(req.downloadHandler.text);
            } catch (System.Exception e) {
                Debug.LogWarning($"[LandParcel] Failed to parse /places/{placeId}/state ({e.Message}).");
                yield break;
            }

            if (state?.props == null) yield break;

            foreach (PropJson p in state.props) {
                // Only props physically placed in THIS land (skip stored / transit / nested).
                if (p.position == null) continue;
                if (p.placeId != state.place?.Id) continue;

                Vector3    worldPos = p.position.ToVector3();
                Quaternion worldRot = Quaternion.Euler(p.rotation != null ? p.rotation.ToVector3() : Vector3.zero);

                PropStateHeader header  = new PropStateHeader { IsBuilt = p.isBuilt, PresetId = p.presetIndex };
                byte[]          payload = StateDataMapper.BuildPayload(p.stateData, header);

                int propId = ServerPropManager.Instance.SpawnProp(
                    roomId:                 this.RoomId,
                    prefabId:               p.configId,
                    position:               worldPos,
                    rotation:               worldRot,
                    initialPayloadOverride: payload,
                    headerOverride:         payload == null ? (PropStateHeader?) header : null,
                    propUuid:               p.Id,
                    propVersion:            p.version,
                    ownerCharId:            this.OccupantCharId
                );
                if (propId < 0) continue;

                TrackProp(propId);

                if (p.forSale)
                    ServerPropManager.Instance.SetSaleState(this.RoomId, propId, true, p.price, this.OccupantCharId);

                GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(propId);
                if (go != null && this.propsContainer != null) {
                    go.transform.SetParent(this.propsContainer);
                    go.transform.position = worldPos;
                    go.transform.rotation = worldRot;
                }
            }
        }

        // ── Client ──────────────────────────────────────────────────────────────

        /// <summary>Apply an occupancy broadcast (S2C_LandOwnership / S2C_LandSnapshot) on the client.
        /// All four fields empty = the parcel is (back) on sale / for rent. A parcel is owned XOR
        /// rented, so at most one of (owner, tenant) is non-empty.</summary>
        public void ClientApplyState(string ownerCharacterId, string ownerDisplayName,
                                     string tenantCharacterId, string tenantDisplayName) {
            this.ownerCharId  = ownerCharacterId;
            this.ownerName    = ownerDisplayName;
            this.tenantCharId = tenantCharacterId;
            this.tenantName   = tenantDisplayName;
            OnClientStateChanged?.Invoke();
        }

        // ── IBuildableSpace ──────────────────────────────────────────────────────

        public bool CanBuild(CharacterData character) =>
            character != null && !string.IsNullOrEmpty(OccupantCharId) && character.Id == OccupantCharId;

        // Land props share the global outdoor room so everyone sees them without an EnterRoom.
        public string RoomId => "city";

        public string PlaceId => landPlaceId;

        public string OwnerCharId => OccupantCharId;

        public Transform PropsContainer => propsContainer;

        public bool HasWalls => false;

        public void TrackProp(int propId) { if (propId > 0) _ownedPropIds.Add(propId); }
        public void UntrackProp(int propId) => _ownedPropIds.Remove(propId);
        public bool OwnsProp(int propId) => _ownedPropIds.Contains(propId);

        // No walls — nothing to hide.
        public void SetWallVisibility(VisibilityModeEnum mode) { }
    }
}
