using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Scriptables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim {
    public class ApartmentController : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Transform frontDoorSpawn;

        [SerializeField]
        private Transform propsContainer;

        [SerializeField]
        private GeographicArea geographicArea;

        [SerializeField]
        private ApartmentPresetConfiguration ahmedConfiguration;

        [SerializeField]
        private ApartmentPresetConfiguration talyahConfiguration;

        [SerializeField]
        private ApartmentPresetConfiguration katarinaConfiguration;

        [SerializeField]
        private CoverSettings defaultWallCoverSettings;

        [SerializeField]
        private CoverSettings defaultGroundCoverSettings;

        [SerializeField]
        private Ground[] grounds;

        [SerializeField]
        private Roof roof;

        [SerializeField]
        private Transform spawnPosition;

        [Header("Only for debug")]
        [SerializeField]
        private Home homeData;

        [SerializeField]
        private CharacterData tenant;

        private string street;
        private int doorNumber;
        private int floorNumber;
        private string tenantId;
        private string tenantFullName;
        private Identity tenantIdentity;
        private string presetName;

        private ApartmentPresetConfiguration currentConfiguration;

        [SerializeField]
        private HallController associatedHallController;

        [SerializeField]
        private ApartmentState state = ApartmentState.NOT_CREATED;

        private bool forcePropsHidden;
        private bool forceWallHidden;

        private readonly Dictionary<int, CoverSettings> coverSettingsByFaces  = new Dictionary<int, CoverSettings>();
        private readonly Dictionary<int, CoverSettings> coverSettingsByGround = new Dictionary<int, CoverSettings>();

        public delegate void VisibilityModeChanged(VisibilityModeEnum mode);

        public static event VisibilityModeChanged OnPropsVisibilityModeChanged;
        public static event VisibilityModeChanged OnWallVisibilityModeChanged;

        private void Awake() {
            this.talyahConfiguration.container.SetActive(false);
            this.ahmedConfiguration.container.SetActive(false);
            this.katarinaConfiguration.container.SetActive(false);

            ClientPropManager.OnRoomStateReceived += OnRoomStateReceived;
        }

        private void OnDestroy() {
            ClientPropManager.OnRoomStateReceived -= OnRoomStateReceived;

            if (NetworkServer.active) {
                ServerApartmentRegistry.Instance.Unregister(this.ApartmentKey);
                ServerPropManager.Instance?.RemoveApartmentState(this.RoomId, this.ApartmentKey);
                foreach (int propId in _ownedPropIds.ToArray())
                    ServerPropManager.Instance?.RemoveProp(this.RoomId, propId);
                _ownedPropIds.Clear();
                _lightPropIds.Clear();
                _innerDoorPropIds.Clear();
                this.frontDoorPropId = 0;
                this.deliveryBoxPropId = 0;
            }
        }

        // ── Server-side init ──────────────────────────────────────────────────

        [Server]
        public void Init(Address newAddress, HallController hallController) {
            this.associatedHallController = hallController;
            this.street     = newAddress.street;
            this.doorNumber = newAddress.doorNumber;
            this.floorNumber = hallController.FloorNumber;

            if (!isActiveAndEnabled) this.roof.gameObject.SetActive(false);

            byte[] frontDoorPayload = new DoorState {
                Header     = PropStateHeader.Default,
                IsOpen     = false,
                LockState  = DoorLockState.LOCKED,
                DoorNumber = newAddress.doorNumber
            }.Serialize();

            int doorPropId = ServerPropManager.Instance.SpawnProp(
                this.RoomId,
                this.frontDoorPrefabConfig.GetId(),
                this.frontDoorSpawn.position,
                this.frontDoorSpawn.rotation,
                frontDoorPayload
            );
            if (doorPropId >= 0) {
                this.frontDoorPropId = doorPropId;
                TrackProp(doorPropId);
                GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(doorPropId);
                if (go != null) {
                    go.transform.SetParent(this.propsContainer);
                    go.transform.position = this.frontDoorSpawn.position;
                    go.transform.rotation = this.frontDoorSpawn.rotation;
                }
            }

            ServerApartmentRegistry.Instance.Register(this.ApartmentKey, this);

            StartCoroutine(RetrieveData());
        }

        [Server]
        public void Regenerate() {
            // Reset to NOT_CREATED so CheckGenerationState counts this apartment as pending,
            // and tell the hall to reset its broadcast guard so it will re-broadcast.
            this.state = ApartmentState.NOT_CREATED;
            this.associatedHallController?.OnApartmentRegenerating();

            foreach (int propId in _ownedPropIds.ToArray())
                ServerPropManager.Instance?.RemoveProp(this.RoomId, propId);
            _ownedPropIds.Clear();
            _lightPropIds.Clear();
            _innerDoorPropIds.Clear();
            this.frontDoorPropId = 0;
            this.deliveryBoxPropId = 0;
            this._frontDoorRestoredFromSave = false;

            // Re-spawn the front door (Init spawns it once; Regenerate must also provide it
            // so the hallway door exists before RetrieveData unlocks/locks it).
            byte[] lockedPayload = new DoorState {
                Header     = PropStateHeader.Default,
                IsOpen     = false,
                LockState  = DoorLockState.LOCKED,
                DoorNumber = this.doorNumber
            }.Serialize();
            int newDoorId = ServerPropManager.Instance.SpawnProp(
                this.RoomId, this.frontDoorPrefabConfig.GetId(),
                this.frontDoorSpawn.position, this.frontDoorSpawn.rotation,
                lockedPayload
            );
            if (newDoorId >= 0) {
                this.frontDoorPropId = newDoorId;
                TrackProp(newDoorId);
            }

            StartCoroutine(RetrieveData());
        }

        // ── Client-side init ──────────────────────────────────────────────────

        /// <summary>
        /// Called on clients (and on server-as-host) once the apartment data is known.
        /// Sets up the visual state: preset configuration, geographic area label.
        /// Cover data arrives later via S2C_RoomState → ApplyRoomState.
        /// </summary>
        public void ClientSetup(string streetName, int doorNum, int floorNum, string preset, HallController hall) {
            this.street     = streetName;
            this.doorNumber = doorNum;
            this.floorNumber = floorNum;
            this.associatedHallController = hall;

            ApplyPresetName(preset);
            UpdateGeographicArea();
            Debug.Log($"[Apartment] Creating apartment runtime id={doorNum} street={streetName} floor={floorNum} preset={preset}");
        }

        // ── RoomState subscription ────────────────────────────────────────────

        private void OnRoomStateReceived(string roomId, byte[] payload) {
            if (string.IsNullOrEmpty(this.street) || roomId != this.RoomId) return;
            ApartmentRoomState state = ApartmentRoomState.Deserialize(payload);
            // Multiple apartments share the hall room — only apply if the payload
            // identifies this specific apartment.
            if (state == null || state.street != this.street || state.doorNumber != this.doorNumber) return;
            ApplyRoomState(state);
        }

        public void ApplyRoomState(ApartmentRoomState state) {
            if (state == null) return;

            if (!string.IsNullOrEmpty(state.tenantId)) {
                this.tenantId = state.tenantId;
            }
            
            if (!string.IsNullOrEmpty(state.tenantFullName))
            {
                this.tenantFullName = state.tenantFullName;
            }

            if (!string.IsNullOrEmpty(state.presetName) && state.presetName != this.presetName) {
                ApplyPresetName(state.presetName);
            }

            if (this.currentConfiguration.walls != null) {
                if (state.walls != null && state.walls.Length > 0) {
                    Dictionary<int, CoverSettings> wallDict = state.walls.ToDictionary(
                        x => x.idx,
                        x => new CoverSettings { paintConfigId = x.paintConfigId, additionalColor = x.GetColor() }
                    );
                    this.currentConfiguration.walls.Setup(wallDict);
                } else {
                    this.currentConfiguration.walls.Setup(new Dictionary<int, CoverSettings>());
                }
            }

            if (state.grounds != null && state.grounds.Length > 0) {
                Dictionary<int, CoverData> groundsDict = state.grounds.ToDictionary(x => x.idx);
                for (int i = 0; i < this.grounds.Length; i++) {
                    if (groundsDict.TryGetValue(i, out CoverData cd)) {
                        this.grounds[i].SetCoverSettings(new CoverSettings {
                            paintConfigId   = cd.paintConfigId,
                            additionalColor = cd.GetColor()
                        });
                    }
                }
            }
        }

        // ── Server: broadcast room state to all players in the room ──────────

        [Server]
        public void UpdateRoomState() {
            ApartmentRoomState state = new ApartmentRoomState {
                street     = this.street,
                doorNumber = this.doorNumber,
                tenantId   = this.tenantId,
                tenantFullName = this.tenantIdentity.FullName,
                presetName = this.presetName,
                walls      = SaveUtils.CreateCoverDatas(this.coverSettingsByFaces),
                grounds    = SaveUtils.CreateCoverDatas(this.coverSettingsByGround)
            };
            ServerPropManager.Instance.SetApartmentState(this.RoomId, this.ApartmentKey, state.Serialize());
        }

        // ── Server: apply cover updates received from client ──────────────────

        [Server]
        public void ServerApplyWallCovers(CoverData[] covers) {
            foreach (var cd in covers) {
                this.coverSettingsByFaces[cd.idx] = new CoverSettings {
                    paintConfigId   = cd.paintConfigId,
                    additionalColor = cd.GetColor()
                };
            }
            UpdateRoomState();
        }

        [Server]
        public void ServerApplyGroundCovers(CoverData[] covers) {
            foreach (var cd in covers) {
                this.coverSettingsByGround[cd.idx] = new CoverSettings {
                    paintConfigId   = cd.paintConfigId,
                    additionalColor = cd.GetColor()
                };
            }
            UpdateRoomState();
        }

        // ── RetrieveData (server coroutine) ───────────────────────────────────

        [Server]
        private IEnumerator RetrieveData() {
            Debug.Log($"[Apartment] RetrieveData start door={this.doorNumber} street={this.street} room={this.RoomId}");
            Address addr = new Address { street = this.street, doorNumber = this.doorNumber, homeType = HomeTypeEnum.APARTMENT };
            UnityWebRequest request = ApiManager.Instance.RetrieveHomeRequest(addr);

            yield return request.SendWebRequest();

            Home homeResponse = JsonUtility.FromJson<Home>(request.downloadHandler.text);

            if (homeResponse?.Id != null) {
                this.homeData   = homeResponse;
                this.presetName = this.homeData.Preset;

                UnityWebRequest tenantRequest = ApiManager.Instance.RetrieveCharacterByIdRequest(this.HomeData.Tenant);

                yield return tenantRequest.SendWebRequest();

                CharacterResponse characterResponse = JsonUtility.FromJson<CharacterResponse>(tenantRequest.downloadHandler.text);

                if (characterResponse?.Characters?.Length > 0) {
                    this.tenant         = characterResponse.Characters[0];
                    this.tenantId       = this.tenant.Id;
                    this.tenantIdentity = this.tenant.Identity;
                } else {
                    Debug.LogError($"[ApartmentController] [RetrieveData] Cannot retrieve tenant data with [tenantId={this.HomeData.Tenant}, homeId={this.HomeData.Id}]");
                }

                ApplyPresetServerSide(this.presetName);

                // Hydrate from /places/:id/state. Fresh / non-hydrated apartments fall
                // back to InstantiateLevelFromPreset (ScriptableObject-driven defaults).
                yield return LoadPlaceStateOrFallback(homeResponse);

                if (this.deliveryBoxPropId > 0 && !string.IsNullOrEmpty(this.tenantId)) {
                    UnityWebRequest countReq = ApiManager.Instance.RetrieveDeliveriesRequest(this.tenantId);
                    yield return countReq.SendWebRequest();
                    if (countReq.responseCode == 200) {
                        DeliveryResponse dr = JsonUtility.FromJson<DeliveryResponse>(countReq.downloadHandler.text);
                        uint count = dr?.Deliveries != null ? (uint)dr.Deliveries.Count : 0;
                        if (ServerPropManager.Instance.TryGetPropState(this.RoomId, this.deliveryBoxPropId, out var propState)) {
                            PropStateHeader header = PropStateHeader.ReadFrom(propState.Payload);
                            byte[] newPayload = new DeliveryBoxState { Header = header, DeliveryCount = count }.Serialize();
                            ServerPropManager.Instance.UpdatePropState(this.RoomId, this.deliveryBoxPropId, newPayload);
                        }
                    }
                }

                // Auto-unlock front door for a new tenant. If the save already carried a
                // lockState for this door (player explicitly locked it before logout), the
                // restore in InstantiateLevel already applied it — don't clobber.
                if (!this._frontDoorRestoredFromSave) {
                    this.SetFrontDoorLockState(DoorLockState.UNLOCKED);
                }
                this.state = ApartmentState.GENERATED;
                UpdateRoomState();
                Debug.Log($"[Apartment] Registered apartment id={this.doorNumber} door={this.doorNumber} tenant={this.tenantId} preset={this.presetName} room={this.RoomId}");
                this.associatedHallController.CheckGenerationState();
            } else {
                this.SetFrontDoorLockState(DoorLockState.LOCKED);
                this.state = ApartmentState.NOT_GENERATED;
                Debug.Log($"[Apartment] Apartment door={this.doorNumber} has no tenant — NOT_GENERATED, room={this.RoomId}");
                this.associatedHallController.CheckGenerationState();
            }
        }

        [Server]
        private void ApplyPresetServerSide(string name) {
            if (name == "ahmed") {
                this.currentConfiguration = this.ahmedConfiguration;
            } else if (name == "talyah") {
                this.currentConfiguration = this.talyahConfiguration;
            } else {
                this.currentConfiguration = this.katarinaConfiguration;
            }
        }

        /// <summary>
        /// Seed an apartment from its preset ScriptableObject — used when the
        /// place has no rows in DB yet (freshly assigned apartment, or HTTP load
        /// failure). Inner doors are persisted lazily by MaterializeUnbridgedDoors
        /// at the end of RetrieveData.
        /// </summary>
        [Server]
        private void InstantiateLevelFromPreset() {
            // Default covers — no saved customization for a fresh apt.
            int wallSlots = this.currentConfiguration.walls.SharedMaterials().Length;
            for (int i = 0; i < wallSlots; i++) {
                this.coverSettingsByFaces[i] = defaultWallCoverSettings;
            }
            for (int i = 0; i < this.grounds.Length; i++) {
                this.coverSettingsByGround[i] = defaultGroundCoverSettings;
            }

            // Preset-driven fixtures.
            SpawnInnerDoorsFromPreset();
            SpawnLightsFromPreset();
            SpawnDeliveryBoxFromPreset();

            // No saved door states for a fresh apt — caller's UNLOCK-for-new-tenant logic kicks in.
            this._frontDoorRestoredFromSave = false;
        }

        private bool _frontDoorRestoredFromSave;

        /// <summary>
        /// Entry point for apartment hydration. Reads /places/:id/state when the
        /// place is hydrated (props/covers populated). Otherwise falls back to
        /// InstantiateLevelFromPreset which seeds from the Unity ScriptableObject —
        /// used for freshly-assigned apartments or when the HTTP call fails.
        /// </summary>
        [Server]
        private IEnumerator LoadPlaceStateOrFallback(Home homeResponse) {
            UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(homeResponse.Id);
            yield return stateReq.SendWebRequest();

            bool usedNewPath = false;
            if (stateReq.responseCode == 200) {
                try {
                    Sim.Entities.Persistence.PlaceStateJson state =
                        Newtonsoft.Json.JsonConvert.DeserializeObject<Sim.Entities.Persistence.PlaceStateJson>(
                            stateReq.downloadHandler.text);

                    if (state?.place != null && PlaceStateIsHydrated(state)) {
                        Debug.Log($"[Apartment] Hydrated from /places/:id/state placeId={state.place.Id} props={state.props?.Length ?? 0} covers={state.covers?.Length ?? 0}");
                        InstantiateLevelFromPlaceState(state);
                        usedNewPath = true;
                    } else {
                        Debug.Log($"[Apartment] Place not yet hydrated (place={state?.place != null} props={state?.props?.Length ?? 0} covers={state?.covers?.Length ?? 0}) — seeding from preset");
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning($"[Apartment] Failed to parse /places/:id/state ({e.Message}) — seeding from preset");
                }
            } else {
                Debug.LogWarning($"[Apartment] /places/{homeResponse.Id}/state failed ({stateReq.responseCode}) — seeding from preset");
            }

            if (!usedNewPath) InstantiateLevelFromPreset();

            // Lazy door materialization — any door (front + inner) without a DB bridge
            // after the load is a fixture-spawned one. POST /props to create the row and
            // bridge the UUID so future state changes (lockState, isOpen) persist via
            // SyncPropState. Idempotent: doors loaded from state.props are already
            // bridged and skipped.
            yield return MaterializeUnbridgedDoors(homeResponse.Id);
        }

        [Server]
        private IEnumerator MaterializeUnbridgedDoors(string placeId) {
            if (string.IsNullOrEmpty(placeId)) yield break;

            List<int> candidates = new List<int>();
            if (this.frontDoorPropId > 0) candidates.Add(this.frontDoorPropId);
            candidates.AddRange(_innerDoorPropIds);

            foreach (int propId in candidates) {
                if (ServerPropManager.Instance.GetBridge(propId) != null) continue;            // already in DB
                if (!ServerPropManager.Instance.TryGetPropState(this.RoomId, propId, out var state)) continue;
                if (state.Type != PropType.Door) continue;

                DoorState ds = DoorState.Deserialize(state.Payload);
                PropStateHeader header = PropStateHeader.ReadFrom(state.Payload);

                Sim.Entities.Persistence.CreatePropBody body = new Sim.Entities.Persistence.CreatePropBody {
                    placeId     = placeId,
                    configId    = state.PrefabId,
                    position    = new Sim.Entities.Persistence.Vector3Body(state.Position),
                    rotation    = new Sim.Entities.Persistence.Vector3Body(state.Rotation.eulerAngles),
                    isBuilt     = header.IsBuilt,
                    presetIndex = header.PresetId,
                    stateData   = new Dictionary<string, object> {
                        { "kind",       "door"             },
                        { "isOpen",     ds.IsOpen          },
                        { "lockState",  (int) ds.LockState },
                        { "doorNumber", ds.DoorNumber      },
                    }
                };

                UnityWebRequest req = ApiManager.Instance.CreatePropRequest(body);
                yield return req.SendWebRequest();

                if (req.responseCode < 200 || req.responseCode >= 300) {
                    Debug.LogWarning($"[Apartment] MaterializeUnbridgedDoors: POST /props failed code={req.responseCode} body={req.downloadHandler?.text}");
                    continue;
                }

                try {
                    Sim.Entities.Persistence.PropJson created =
                        Newtonsoft.Json.JsonConvert.DeserializeObject<Sim.Entities.Persistence.PropJson>(req.downloadHandler.text);
                    if (created != null && !string.IsNullOrEmpty(created.Id)) {
                        ServerPropManager.Instance.AssociateUuid(propId, created.Id, created.version);
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning($"[Apartment] MaterializeUnbridgedDoors: failed to parse response ({e.Message})");
                }
            }
        }

        /// <summary>
        /// True iff the place has at least one persisted row (cover or prop).
        /// False means the apartment was newly assigned and never touched —
        /// caller seeds from the Unity ScriptableObject preset instead.
        /// </summary>
        public static bool PlaceStateIsHydrated(Sim.Entities.Persistence.PlaceStateJson state) {
            if (state == null) return false;
            bool hasCovers = state.covers != null && state.covers.Length > 0;
            bool hasProps  = state.props  != null && state.props.Length  > 0;
            return hasCovers || hasProps;
        }

        /// <summary>
        /// Hydrate the apartment from a PlaceStateJson fetched via GET /places/:id/state.
        /// Consumes the relational shape (places + props + covers).
        /// </summary>
        [Server]
        public void InstantiateLevelFromPlaceState(Sim.Entities.Persistence.PlaceStateJson state) {
            if (state == null) {
                Debug.LogWarning("[Apartment] InstantiateLevelFromPlaceState: null state");
                return;
            }

            // 1. Covers — populate the cover dictionaries directly (no C2S broadcast at load).
            Dictionary<int, CoverSettings> wallSettings   = new Dictionary<int, CoverSettings>();
            Dictionary<int, CoverSettings> groundSettings = new Dictionary<int, CoverSettings>();
            if (state.covers != null) {
                foreach (var c in state.covers) {
                    Color color = c.color != null && c.color.Length >= 3
                        ? new Color(c.color[0], c.color[1], c.color[2])
                        : Color.white;
                    CoverSettings cs = new CoverSettings { paintConfigId = c.paintConfigId, additionalColor = color };
                    if (c.surfaceKind == "wall")   wallSettings[c.surfaceIndex]   = cs;
                    if (c.surfaceKind == "ground") groundSettings[c.surfaceIndex] = cs;
                }
            }
            int wallSlots = this.currentConfiguration.walls.SharedMaterials().Length;
            for (int i = 0; i < wallSlots; i++) {
                this.coverSettingsByFaces[i] = wallSettings.ContainsKey(i) ? wallSettings[i] : defaultWallCoverSettings;
            }
            for (int i = 0; i < this.grounds.Length; i++) {
                this.coverSettingsByGround[i] = groundSettings.ContainsKey(i) ? groundSettings[i] : defaultGroundCoverSettings;
            }

            // 2. Fixtures from the preset FIRST — inner doors, delivery box. These
            // positions are deterministic from the ScriptableObject and we trust them
            // over whatever state.props might carry (backfill could have stored local
            // coords instead of world, etc.). State (lockState, isOpen) and UUID
            // bridge are restored from state.props by position-matching below.
            SpawnInnerDoorsFromPreset();
            SpawnDeliveryBoxFromPreset();

            // 3. Props from state.props — each carries its own UUID + version.
            // Inner doors are matched to a preset-spawned door by position rather than
            // re-spawned (avoids duplicates and trusts preset positions). Front door
            // is also state-applied to the Init()-spawned door, not respawned.
            int lightConfigId = this.lightPrefabConfig != null ? this.lightPrefabConfig.GetId() : -1;
            int savedLightsSpawned = 0;
            if (state.props != null) {
                foreach (Sim.Entities.Persistence.PropJson p in state.props) {
                    // Skip props that aren't physically placed in THIS apt (stored / transit / nested).
                    if (p.position == null) continue;
                    if (p.placeId != state.place?.Id) continue;

                    if (IsFrontDoorEntry(p)) {
                        RestoreFrontDoorFromPlaceEntry(p);
                        continue;
                    }

                    if (IsInnerDoorEntry(p)) {
                        RestoreInnerDoorFromPlaceEntry(p);
                        continue;
                    }

                    Vector3    worldPos  = p.position.ToVector3();
                    Quaternion worldRot  = Quaternion.Euler(p.rotation != null ? p.rotation.ToVector3() : Vector3.zero);

                    PropStateHeader header = new PropStateHeader { IsBuilt = p.isBuilt, PresetId = p.presetIndex };
                    byte[] payload = Sim.Entities.Persistence.StateDataMapper.BuildPayload(p.stateData, header);

                    int propId = ServerPropManager.Instance.SpawnProp(
                        roomId:                 this.RoomId,
                        prefabId:               p.configId,
                        position:               worldPos,
                        rotation:               worldRot,
                        initialPayloadOverride: payload,
                        headerOverride:         payload == null ? (PropStateHeader?) header : null,
                        propUuid:               p.Id,
                        propVersion:            p.version,
                        ownerCharId:            this.tenantId
                    );
                    if (propId < 0) continue;

                    TrackProp(propId);
                    if (p.configId == lightConfigId) {
                        _lightPropIds.Add(propId);
                        savedLightsSpawned++;
                    }

                    // Restore the for-sale listing so visitors see it on entry.
                    if (p.forSale)
                        ServerPropManager.Instance.SetSaleState(this.RoomId, propId, true, p.price, this.tenantId);

                    GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(propId);
                    if (go != null && this.propsContainer != null) {
                        go.transform.SetParent(this.propsContainer);
                        go.transform.position = worldPos;
                        go.transform.rotation = worldRot;
                    }
                }
            }

            // 4. Lights from preset if state.props had none (user never moved them).
            if (savedLightsSpawned == 0) SpawnLightsFromPreset();
        }

        /// <summary>
        /// Match a state.props inner-door entry to a preset-spawned door by world
        /// position (tolerance 0.25u). On match: bridge UUID + version, apply saved
        /// state (lockState, isOpen with locked-must-be-closed invariant).
        /// </summary>
        [Server]
        private void RestoreInnerDoorFromPlaceEntry(Sim.Entities.Persistence.PropJson p) {
            if (p.position == null) return;
            Vector3 dbPos = p.position.ToVector3();

            int matchedPropId = -1;
            float bestDist = 0.25f;
            foreach (int propId in _innerDoorPropIds) {
                if (!ServerPropManager.Instance.TryGetPropState(this.RoomId, propId, out var state)) continue;
                float d = Vector3.Distance(state.Position, dbPos);
                if (d < bestDist) { bestDist = d; matchedPropId = propId; }
            }
            if (matchedPropId < 0) {
                Debug.LogWarning($"[Apartment] Inner door from DB {p.Id} (pos={dbPos}) — no preset match within 0.25u, ignoring DB row");
                return;
            }

            if (!ServerPropManager.Instance.TryGetPropState(this.RoomId, matchedPropId, out var current)) return;
            DoorState ds = DoorState.Deserialize(current.Payload);
            if (p.stateData != null) {
                if (p.stateData.TryGetValue("lockState", out object ls) && int.TryParse(ls?.ToString(), out int lockInt))
                    ds.LockState = (DoorLockState) lockInt;
                if (p.stateData.TryGetValue("isOpen", out object io) && bool.TryParse(io?.ToString(), out bool isOpen))
                    ds.IsOpen = isOpen;
            }
            if (ds.LockState == DoorLockState.LOCKED) ds.IsOpen = false;
            ServerPropManager.Instance.UpdatePropState(this.RoomId, matchedPropId, ds.Serialize());
            ServerPropManager.Instance.AssociateUuid(matchedPropId, p.Id, p.version);
        }

        [Server]
        private bool IsInnerDoorEntry(Sim.Entities.Persistence.PropJson p) {
            if (p.stateData == null) return false;
            if (!p.stateData.TryGetValue("kind", out object kind) || kind?.ToString() != "door") return false;
            // Inner doors carry doorNumber=0 (or missing). A door whose doorNumber matches
            // the apartment is the front door — handled separately by IsFrontDoorEntry.
            if (!p.stateData.TryGetValue("doorNumber", out object dn) || dn == null) return true;
            return int.TryParse(dn.ToString(), out int doorNum) && doorNum != this.doorNumber;
        }

        [Server]
        private void SpawnInnerDoorsFromPreset() {
            if (this.currentConfiguration.doorSpawners == null) return;
            if (this.simpleDoorPrefabConfig == null) return;

            foreach (var spawner in this.currentConfiguration.doorSpawners) {
                if (spawner == null) continue;
                int innerDoorPropId = ServerPropManager.Instance.SpawnProp(
                    this.RoomId,
                    this.simpleDoorPrefabConfig.GetId(),
                    spawner.position,
                    spawner.rotation
                );
                if (innerDoorPropId < 0) continue;
                TrackProp(innerDoorPropId);
                _innerDoorPropIds.Add(innerDoorPropId);
                GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(innerDoorPropId);
                if (go != null) {
                    go.transform.SetParent(this.transform);
                    go.transform.position = spawner.position;
                    go.transform.rotation = spawner.rotation;
                }
            }
        }

        [Server]
        private void SpawnLightsFromPreset() {
            if (this.currentConfiguration.lightSpawns == null) return;
            if (this.lightPrefabConfig == null) return;

            foreach (Transform spawner in this.currentConfiguration.lightSpawns) {
                if (spawner == null) continue;
                int lightPropId = ServerPropManager.Instance.SpawnProp(
                    this.RoomId,
                    this.lightPrefabConfig.GetId(),
                    spawner.position,
                    spawner.rotation
                );
                if (lightPropId < 0) continue;
                TrackProp(lightPropId);
                _lightPropIds.Add(lightPropId);
                GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(lightPropId);
                if (go != null && this.propsContainer != null) {
                    go.transform.SetParent(this.propsContainer);
                    go.transform.position = spawner.position;
                    go.transform.rotation = spawner.rotation;
                }
            }
        }

        [Server]
        private bool IsFrontDoorEntry(Sim.Entities.Persistence.PropJson p) {
            if (p.stateData == null) return false;
            if (!p.stateData.TryGetValue("kind", out object kind) || kind?.ToString() != "door") return false;
            if (!p.stateData.TryGetValue("doorNumber", out object dn) || dn == null) return false;
            return int.TryParse(dn.ToString(), out int doorNum) && doorNum == this.doorNumber;
        }

        [Server]
        private void RestoreFrontDoorFromPlaceEntry(Sim.Entities.Persistence.PropJson p) {
            if (this.frontDoorPropId <= 0) return;
            if (!ServerPropManager.Instance.TryGetPropState(this.RoomId, this.frontDoorPropId, out var current)) return;

            DoorState ds = DoorState.Deserialize(current.Payload);
            if (p.stateData.TryGetValue("lockState", out object ls) && int.TryParse(ls?.ToString(), out int lockInt))
                ds.LockState = (DoorLockState) lockInt;
            if (p.stateData.TryGetValue("isOpen", out object io) && bool.TryParse(io?.ToString(), out bool isOpen))
                ds.IsOpen = isOpen;
            // A locked door must visually be closed at load — sanitize stale saves
            // where the player locked while the door was still ajar.
            if (ds.LockState == DoorLockState.LOCKED) ds.IsOpen = false;
            ServerPropManager.Instance.UpdatePropState(this.RoomId, this.frontDoorPropId, ds.Serialize());
            ServerPropManager.Instance.AssociateUuid(this.frontDoorPropId, p.Id, p.version);
            this._frontDoorRestoredFromSave = true;
        }

        [Server]
        private void SpawnDeliveryBoxFromPreset() {
            if (this.deliveryBoxPropId > 0) return;        // already spawned (idempotency)

            int boxId = ServerPropManager.Instance.SpawnProp(
                this.RoomId,
                this.deliveryBoxPrefabConfig.GetId(),
                this.currentConfiguration.deliveryBoxSpawn.position,
                this.currentConfiguration.deliveryBoxSpawn.rotation,
                ownerCharId: this.tenantId
            );
            if (boxId < 0) return;
            TrackProp(boxId);
            GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(boxId);
            if (go != null && this.propsContainer != null) {
                go.transform.SetParent(this.propsContainer);
                go.transform.position = this.currentConfiguration.deliveryBoxSpawn.position;
                go.transform.rotation = this.currentConfiguration.deliveryBoxSpawn.rotation;
            }
            this.deliveryBoxPropId = boxId;
        }

        [Server]
        private void SetFrontDoorLockState(DoorLockState newState) {
            if (this.frontDoorPropId <= 0) return;
            if (!ServerPropManager.Instance.TryGetPropState(this.RoomId, this.frontDoorPropId, out var state)) return;
            DoorState current = DoorState.Deserialize(state.Payload);
            if (current.LockState == newState) return;
            current.LockState = newState;
            ServerPropManager.Instance.UpdatePropState(this.RoomId, this.frontDoorPropId, current.Serialize());
        }

        // ── Client: apply cover preview ───────────────────────────────────────

        public void ApplyWallSettings() {
            CoverData[] covers = SaveUtils.CreateCoverDatas(this.currentConfiguration.walls.CoverSettingsInPreview);
            this.currentConfiguration.walls.ApplyModification();
            NetworkClient.Send(new C2S_ApplyWallCovers {
                RoomId      = this.RoomId,
                CoversJson  = new CoverDataWrapper { items = covers }.Serialize()
            });
        }

        public void ApplyGroundSettings() {
            Ground[] groundFiltered = this.grounds.Where(x => x.IsPreview()).ToArray();
            Dictionary<int, CoverSettings> groundDataToUpdate = groundFiltered.ToDictionary(
                x => Array.IndexOf(grounds, x), x => x.CurrentCover
            );
            foreach (var ground in groundFiltered) ground.ApplyModification();
            CoverData[] covers = SaveUtils.CreateCoverDatas(groundDataToUpdate);
            NetworkClient.Send(new C2S_ApplyGroundCovers {
                RoomId     = this.RoomId,
                CoversJson = new CoverDataWrapper { items = covers }.Serialize()
            });
        }

        // ── Visibility helpers ────────────────────────────────────────────────

        public void SetPropsVisibility(VisibilityModeEnum mode) {
            this.forcePropsHidden = mode == VisibilityModeEnum.FORCE_HIDE;
            this.UpdatePropsVisibility(mode);
        }

        public void TogglePropsVisible() {
            this.forcePropsHidden = !this.forcePropsHidden;
            this.UpdatePropsVisibility(this.forcePropsHidden ? VisibilityModeEnum.FORCE_HIDE : VisibilityModeEnum.AUTO);
        }

        private void UpdatePropsVisibility(VisibilityModeEnum mode) {
            foreach (PropsRenderer pr in GetComponentsInChildren<PropsRenderer>()) {
                if (pr != null && pr.IsHideable()) pr.SetVisibilityMode(mode);
            }
            OnPropsVisibilityModeChanged?.Invoke(mode);
        }

        public void ResetWallPreview()   => this.currentConfiguration.walls.Reset();

        public void ResetGroundPreview() =>
            this.grounds.Where(x => x.IsPreview()).ToList().ForEach(x => x.ResetPreview());

        #region Wall Visibility Management

        public void SetWallVisibility(VisibilityModeEnum mode) {
            this.forceWallHidden = mode == VisibilityModeEnum.FORCE_HIDE;
            this.UpdateWallVisibility(mode);
        }

        private void UpdateWallVisibility(VisibilityModeEnum mode) {
            if (mode == VisibilityModeEnum.FORCE_HIDE) {
                this.currentConfiguration.walls.HideWalls();
                this.currentConfiguration.shortWalls.SetActive(true);
            } else {
                this.currentConfiguration.walls.Reset();
                this.currentConfiguration.shortWalls.SetActive(false);
            }
            OnWallVisibilityModeChanged?.Invoke(mode);
        }

        public void ToggleWallVisibility() {
            this.forceWallHidden = !this.forceWallHidden;
            this.UpdateWallVisibility(this.forceWallHidden ? VisibilityModeEnum.FORCE_HIDE : VisibilityModeEnum.AUTO);
        }

        #endregion

        // ── Internal helpers ──────────────────────────────────────────────────

        private void ApplyPresetName(string name) {
            // Empty preset = unoccupied apartment: skip the interior geometry entirely.
            // The roof and the (locked) front door stay active so the building still reads
            // visually from outside, but walls/grounds aren't instantiated for nothing.
            if (string.IsNullOrEmpty(name)) {
                ApplyUnoccupiedState();
                return;
            }

            this.presetName = name;

            if (name == "ahmed") {
                this.currentConfiguration = this.ahmedConfiguration;
            } else if (name == "talyah") {
                this.currentConfiguration = this.talyahConfiguration;
            } else {
                this.currentConfiguration = this.katarinaConfiguration;
            }

            this.currentConfiguration.container.SetActive(true);
            SetGroundsActive(true);
            Debug.Log($"[Apartment] Applying door number {this.doorNumber} preset={name}");
        }

        /// <summary>
        /// Visual state for an apartment with no tenant: no preset container, no grounds.
        /// Roof and front door remain active.
        /// </summary>
        private void ApplyUnoccupiedState() {
            this.presetName = null;
            // Awake already deactivates all preset containers, but redo it to cover the
            // occupied → unoccupied transition (if it ever happens at runtime).
            this.talyahConfiguration.container.SetActive(false);
            this.ahmedConfiguration.container.SetActive(false);
            this.katarinaConfiguration.container.SetActive(false);
            SetGroundsActive(false);
            Debug.Log($"[Apartment] Door {this.doorNumber}: unoccupied — keeping roof + front door only");
        }

        private void SetGroundsActive(bool active) {
            if (this.grounds == null) return;
            foreach (Ground g in this.grounds) {
                if (g != null) g.gameObject.SetActive(active);
            }
        }

        private void UpdateGeographicArea() {
            if (this.geographicArea != null) {
                this.geographicArea.LocationText =
                    $"{this.street}, Étage {this.floorNumber}, Porte {this.doorNumber}";
            }
        }

        // ── Properties ────────────────────────────────────────────────────────

        public Address Address => new Address { street = this.street, doorNumber = this.doorNumber, homeType = HomeTypeEnum.APARTMENT };

        public Transform SpawnPosition => spawnPosition;

        // The "room" is the whole hall (one room per floor). All apartments on the same
        // floor share this room id for prop sync. Use ApartmentKey to identify a specific apt.
        public string RoomId => associatedHallController != null ? associatedHallController.RoomId : $"apt:{street}:{doorNumber}";

        // Stable per-apartment identifier (independent of RoomId).
        public string ApartmentKey => $"apt:{street}:{doorNumber}";

        public Transform PropsContainer => propsContainer;

        public ApartmentState State => state;

        public Home HomeData {
            get => homeData;
            set => homeData = value;
        }

        public string TenantId => tenantId;

        public string TenantFullName => tenantFullName;

        public Identity TenantIdentity => tenantIdentity;

        public bool IsTenant(CharacterData character) => character.Id == this.tenantId;

        public string PresetName => presetName;

        public int FrontDoorPropId => frontDoorPropId;
        private int frontDoorPropId;

        // Tracks every prop spawned by this apartment so OnDestroy/Regenerate can clean up
        // only this apt's props (the room is shared with the rest of the hall).
        private readonly HashSet<int> _ownedPropIds = new HashSet<int>();
        public void TrackProp(int propId) { if (propId > 0) _ownedPropIds.Add(propId); }
        public void UntrackProp(int propId) { _ownedPropIds.Remove(propId); }
        public bool OwnsProp(int propId) => _ownedPropIds.Contains(propId);

        [Header("Prop Config")]
        [SerializeField, Tooltip("PropsConfig of the front door prefab. Must be under Resources/Configurations/Props with a unique ID.")]
        private PropsConfig frontDoorPrefabConfig;

        [SerializeField, Tooltip("PropsConfig of the inner (simple) door prefab. Must be under Resources/Configurations/Props with a unique ID.")]
        private PropsConfig simpleDoorPrefabConfig;

        public int DeliveryBoxPropId => deliveryBoxPropId;
        private int deliveryBoxPropId;

        [SerializeField, Tooltip("PropsConfig of the delivery box prefab. Must be under Resources/Configurations/Props with a unique ID.")]
        private PropsConfig deliveryBoxPrefabConfig;

        [SerializeField, Tooltip("PropsConfig of the ceiling light prefab. Must be under Resources/Configurations/Props with a unique ID.")]
        private PropsConfig lightPrefabConfig;

        // Tracks light prop ids — used by ApartmentController's cover/visibility
        // helpers to identify which props are ceiling fixtures rather than user-placed.
        private readonly HashSet<int> _lightPropIds = new HashSet<int>();

        // Tracks inner door prop ids spawned during this load — used by the door
        // lazy-materialization step to know which fixtures still need a DB row.
        private readonly HashSet<int> _innerDoorPropIds = new HashSet<int>();
    }

    [Serializable]
    public struct ApartmentPresetConfiguration {
        public GameObject container;
        public Wall walls;
        public GameObject shortWalls;
        public Transform[] doorSpawners;
        public Transform deliveryBoxSpawn;
        public Transform[] lightSpawns;
    }

    [Serializable]
    public enum ApartmentState {
        NOT_CREATED,
        GENERATED,
        NOT_GENERATED
    }
}
