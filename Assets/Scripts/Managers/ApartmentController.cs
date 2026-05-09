using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Scriptables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

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
            this.frontDoorPropId = 0;
            this.deliveryBoxPropId = 0;
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

        // ── Save ──────────────────────────────────────────────────────────────

        [Server]
        public IEnumerator Save() {
            yield return null;

            Debug.Log("Save home....");
            UnityWebRequest request = ApiManager.Instance.SaveHomeRequest(this.homeData, this.GenerateSceneData());

            yield return request.SendWebRequest();

            if (request.responseCode == 200) {
                Debug.Log("Saved successfully");
            } else {
                Debug.Log("Not saved");
            }
        }

        private void SaveLocal() {
            String sceneDataJson = JsonUtility.ToJson(this.GenerateSceneData());
            File.WriteAllText(Application.dataPath + "/Resources/PresetSceneDatas/" + SceneManager.GetActiveScene().name + ".json", sceneDataJson);
            Debug.Log("Saved locally");
        }

        // ── RetrieveData (server coroutine) ───────────────────────────────────

        [Server]
        private IEnumerator RetrieveData() {
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
                InstantiateLevel(homeResponse.SceneData);

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

                this.SetFrontDoorLockState(DoorLockState.UNLOCKED);
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

        [Server]
        private void InstantiateLevel(SceneData sceneData) {
            sceneData.buckets?.ToList().ForEach(data => { SaveUtils.SpawnPropFromSave(data, this); });
            sceneData.props?.ToList().ForEach(data => { SaveUtils.SpawnPropFromSave(data, this); });

            if (sceneData.walls != null) {
                Dictionary<int, CoverSettings> wallSettings = sceneData.walls.ToDictionary(
                    x => x.idx,
                    x => new CoverSettings { paintConfigId = x.paintConfigId, additionalColor = x.GetColor() }
                );
                for (int i = 0; i < this.currentConfiguration.walls.SharedMaterials().Length; i++) {
                    this.coverSettingsByFaces[i] = wallSettings.ContainsKey(i) ? wallSettings[i] : defaultWallCoverSettings;
                }
            } else {
                for (int i = 0; i < this.currentConfiguration.walls.SharedMaterials().Length; i++) {
                    this.coverSettingsByFaces[i] = defaultWallCoverSettings;
                }
            }

            if (sceneData.grounds != null) {
                Dictionary<int, CoverSettings> groundSettings = sceneData.grounds.ToDictionary(
                    x => x.idx,
                    x => new CoverSettings { paintConfigId = x.paintConfigId, additionalColor = x.GetColor() }
                );
                for (int i = 0; i < this.grounds.Length; i++) {
                    this.coverSettingsByGround[i] = groundSettings.ContainsKey(i) ? groundSettings[i] : defaultGroundCoverSettings;
                }
            } else {
                for (int i = 0; i < this.grounds.Length; i++) {
                    this.coverSettingsByGround[i] = defaultGroundCoverSettings;
                }
            }

            foreach (var spawner in this.currentConfiguration.doorSpawners) {
                int innerDoorPropId = ServerPropManager.Instance.SpawnProp(
                    this.RoomId,
                    this.simpleDoorPrefabConfig.GetId(),
                    spawner.position,
                    spawner.rotation
                );
                if (innerDoorPropId >= 0) {
                    TrackProp(innerDoorPropId);
                    GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(innerDoorPropId);
                    if (go != null) {
                        go.transform.SetParent(this.transform);
                        go.transform.position = spawner.position;
                        go.transform.rotation = spawner.rotation;
                    }
                }
            }

            int deliveryBoxPropId = ServerPropManager.Instance.SpawnProp(
                this.RoomId,
                this.deliveryBoxPrefabConfig.GetId(),
                this.currentConfiguration.deliveryBoxSpawn.position,
                this.currentConfiguration.deliveryBoxSpawn.rotation
            );
            if (deliveryBoxPropId >= 0) {
                TrackProp(deliveryBoxPropId);
                GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(deliveryBoxPropId);
                if (go != null) {
                    go.transform.SetParent(this.propsContainer);
                    go.transform.position = this.currentConfiguration.deliveryBoxSpawn.position;
                    go.transform.rotation = this.currentConfiguration.deliveryBoxSpawn.rotation;
                }
                this.deliveryBoxPropId = deliveryBoxPropId;
            }
        }

        [Server]
        private SceneData GenerateSceneData() {
            List<BucketData>  buckets = new List<BucketData>();
            List<DefaultData> props   = new List<DefaultData>();

            foreach (int propId in _ownedPropIds) {
                if (propId == this.frontDoorPropId) continue;
                if (propId == this.deliveryBoxPropId) continue;
                if (!ServerPropManager.Instance.TryGetPropState(this.RoomId, propId, out var state)) continue;
                if (state.IsScene) continue;

                if (state.Type == PropType.PaintBucket) {
                    buckets.Add(new BucketData(state, this.propsContainer));
                } else {
                    props.Add(new DefaultData(state, this.propsContainer));
                }
            }

            return new SceneData {
                walls   = SaveUtils.CreateCoverDatas(this.coverSettingsByFaces),
                grounds = SaveUtils.CreateCoverDatas(this.coverSettingsByGround),
                buckets = buckets.ToArray(),
                props   = props.ToArray()
            };
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
            // Always fall back to "talyah" so unoccupied apartments still have visible geometry.
            if (string.IsNullOrEmpty(name)) name = "talyah";
            this.presetName = name;

            if (name == "ahmed") {
                this.currentConfiguration = this.ahmedConfiguration;
            } else if (name == "talyah") {
                this.currentConfiguration = this.talyahConfiguration;
            } else {
                this.currentConfiguration = this.katarinaConfiguration;
            }

            this.currentConfiguration.container.SetActive(true);
            Debug.Log($"[Apartment] Applying door number {this.doorNumber} preset={name}");
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

        public Identity TenantIdentity => tenantIdentity;

        public bool IsTenant(CharacterData character) => character.Id == this.tenantId;

        public string PresetName => presetName;

        public int FrontDoorPropId => frontDoorPropId;
        private int frontDoorPropId;

        // Tracks every prop spawned by this apartment so OnDestroy/Regenerate can clean up
        // only this apt's props (the room is shared with the rest of the hall).
        private readonly HashSet<int> _ownedPropIds = new HashSet<int>();
        public void TrackProp(int propId) { if (propId > 0) _ownedPropIds.Add(propId); }
        public bool OwnsProp(int propId) => _ownedPropIds.Contains(propId);

        [Header("Prop Config")]
        [SerializeField, Tooltip("PropsConfig of the front door prefab. Must be in PropsDatabase.")]
        private PropsConfig frontDoorPrefabConfig;

        [SerializeField, Tooltip("PropsConfig of the inner (simple) door prefab. Must be in PropsDatabase.")]
        private PropsConfig simpleDoorPrefabConfig;

        public int DeliveryBoxPropId => deliveryBoxPropId;
        private int deliveryBoxPropId;

        [SerializeField, Tooltip("PropsConfig of the delivery box prefab. Must be in PropsDatabase.")]
        private PropsConfig deliveryBoxPrefabConfig;
    }

    [Serializable]
    public struct ApartmentPresetConfiguration {
        public GameObject container;
        public Wall walls;
        public GameObject shortWalls;
        public Transform[] doorSpawners;
        public Transform deliveryBoxSpawn;
    }

    [Serializable]
    public enum ApartmentState {
        NOT_CREATED,
        GENERATED,
        NOT_GENERATED
    }
}
