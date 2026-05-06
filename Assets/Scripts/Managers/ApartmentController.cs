using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Utils;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Sim {
    public class ApartmentController : NetworkEntity {
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

        [SyncVar(hook = nameof(OnSetAddress))]
        [SerializeField]
        private Address address;

        [SerializeField]
        private CharacterData tenant;

        [SyncVar]
        [SerializeField]
        private string tenantId;
        
        [SyncVar]
        [SerializeField]
        private Identity tenantIdentity;

        [SyncVar(hook = nameof(OnSetPresetName))]
        [SerializeField]
        private string presetName;

        private ApartmentPresetConfiguration currentConfiguration;

        [SerializeField]
        private HallController associatedHallController;

        [SerializeField]
        private ApartmentState state = ApartmentState.NOT_CREATED;

        private bool forcePropsHidden;

        private bool forceWallHidden;

        private readonly SyncDictionary<int, CoverSettings> coverSettingsByFaces = new SyncDictionary<int, CoverSettings>();

        private readonly SyncDictionary<int, CoverSettings> coverSettingsByGround = new SyncDictionary<int, CoverSettings>();

        public delegate void VisibilityModeChanged(VisibilityModeEnum mode);

        public static event VisibilityModeChanged OnPropsVisibilityModeChanged;

        public static event VisibilityModeChanged OnWallVisibilityModeChanged;

        private void Awake() {
            this.talyahConfiguration.container.SetActive(false);
            this.ahmedConfiguration.container.SetActive(false);
            this.katarinaConfiguration.container.SetActive(false);
        }

        [Command(requiresAuthority = false)]
        public void CmdSaveHome(NetworkConnectionToClient sender = null) {
            StartCoroutine(this.Save());
        }

        public override void OnStartServer() {
            base.OnStartServer();

            if (!isClient) {
                this.roof.gameObject.SetActive(false);
            }
        }

        private void OnSetAddress(Address old, Address newValue) {
            this.address = newValue;

            if (NetworkClient.spawned.ContainsKey(ParentId)) {
                this.geographicArea.LocationText =
                    $"{this.address.street}, Étage {NetworkClient.spawned[ParentId].GetComponent<HallController>().FloorNumber}, Porte {this.address.doorNumber}";
            }
        }

        public Address Address => address;

        public Transform SpawnPosition => spawnPosition;

        /// <summary>
        /// Stable id used by the new prop system to scope props per apartment.
        /// Format: "apt:{street}:{doorNumber}".
        /// </summary>
        public string RoomId => $"apt:{address.street}:{address.doorNumber}";

        private void OnSetPresetName(string old, string newValue) {
            this.presetName = newValue;

            if (this.presetName == "ahmed") {
                this.currentConfiguration = this.ahmedConfiguration;
            } else if (this.presetName == "talyah") {
                this.currentConfiguration = this.talyahConfiguration;
            } else {
                this.currentConfiguration = this.katarinaConfiguration;
            }

            this.currentConfiguration.container.SetActive(true);

            this.coverSettingsByFaces.OnChange += OnWallSettingsChanged;
            this.coverSettingsByGround.OnChange += OnGroundSettingsChanged;

            for (int i = 0; i < this.grounds.Length; i++) {
                if (this.coverSettingsByGround.ContainsKey(i)) {
                    this.grounds[i].SetCoverSettings(this.coverSettingsByGround[i]);
                }
            }

            this.currentConfiguration.walls.Setup(this.coverSettingsByFaces.ToDictionary(x => x.Key, x => x.Value));
        }

        public void SetPropsVisibility(VisibilityModeEnum mode) {
            this.forcePropsHidden = mode == VisibilityModeEnum.FORCE_HIDE;

            this.UpdatePropsVisibility(mode);
        }

        public void TogglePropsVisible() {
            this.forcePropsHidden = !this.forcePropsHidden;

            this.UpdatePropsVisibility(this.forcePropsHidden ? VisibilityModeEnum.FORCE_HIDE : VisibilityModeEnum.AUTO);
        }

        private void UpdatePropsVisibility(VisibilityModeEnum mode) {
            // Cover both the legacy Props NetworkBehaviours (still used for doors)
            // and the new system's PropsRenderer attached to behaviours under propsContainer.
            foreach (PropsRenderer pr in GetComponentsInChildren<PropsRenderer>()) {
                if (pr != null && pr.IsHideable()) {
                    pr.SetVisibilityMode(mode);
                }
            }

            OnPropsVisibilityModeChanged?.Invoke(mode);
        }
        
        protected override void AssignParent() {
            Transform curTransform = this.transform;
            Vector3 position = curTransform.position;

            if (NetworkClient.spawned.ContainsKey(ParentId)) {
                this.associatedHallController = NetworkClient.spawned[ParentId].GetComponent<HallController>();
                curTransform.SetParent(this.associatedHallController.transform);
                curTransform.localPosition = position;

                this.geographicArea.LocationText = $"{this.address.street}, Étage {this.associatedHallController.FloorNumber}, Porte {this.address.doorNumber}";
            } else {
                Debug.LogError($"[ApartmentController] [AssignParent] Parent identity not found for apartment {this.name}");
            }
        }

        public override void OnStopClient() {
            base.OnStopClient();

            this.coverSettingsByFaces.OnChange -= OnWallSettingsChanged;
            this.coverSettingsByGround.OnChange -= OnGroundSettingsChanged;
        }

        private void OnWallSettingsChanged(SyncIDictionary<int, CoverSettings>.Operation operation, int key, CoverSettings item) {
            this.currentConfiguration.walls.Setup(this.coverSettingsByFaces.ToDictionary(x => x.Key, x => x.Value));
        }

        private void OnGroundSettingsChanged(SyncIDictionary<int, CoverSettings>.Operation operation, int key, CoverSettings item) {
            this.grounds[key].SetCoverSettings(item);
        }

        public Transform PropsContainer => propsContainer;

        [Server]
        public IEnumerator Save() {
            // TODO: handle save queue
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

        [Server]
        public void Init(Address newAddress, HallController hallController) {
            this.associatedHallController = hallController;
            this.address = newAddress;

            // Spawn the front door in the CITY room so hall players see open/close animations.
            // Lock state and door number are part of the DoorState payload.
            byte[] frontDoorPayload = new DoorState {
                Header     = PropStateHeader.Default,
                IsOpen     = false,
                LockState  = DoorLockState.LOCKED,
                DoorNumber = newAddress.doorNumber
            }.Serialize();

            int doorPropId = ServerPropManager.Instance.SpawnProp(
                FrontDoorRoomId,
                this.frontDoorPrefabConfigId,
                this.frontDoorSpawn.position,
                this.frontDoorSpawn.rotation,
                frontDoorPayload
            );
            if (doorPropId >= 0) {
                this.frontDoorPropId = doorPropId;
                GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(doorPropId);
                if (go != null) {
                    go.transform.SetParent(this.propsContainer);
                    go.transform.position = this.frontDoorSpawn.position;
                    go.transform.rotation = this.frontDoorSpawn.rotation;
                }
            }

            StartCoroutine(RetrieveData());
        }

        [Server]
        public void Regenerate() {
            // Wipe any previously-spawned props (apt room) before re-instantiating
            // (avoids stacking duplicates on tenant change / hot reload).
            ServerPropManager.Instance.ClearRoom(this.RoomId);
            this.deliveryBoxPropId = 0;
            // Note: front door is in the city room; we keep it but reset its number/lock if needed.
            StartCoroutine(RetrieveData());
        }

        private void OnDestroy() {
            if (NetworkServer.active) {
                ServerPropManager.Instance.ClearRoom(this.RoomId);
                if (this.frontDoorPropId > 0) {
                    ServerPropManager.Instance.RemoveProp(FrontDoorRoomId, this.frontDoorPropId);
                }
            }
        }

        /// <summary>Updates the front door lock state via the new prop system.</summary>
        [Server]
        private void SetFrontDoorLockState(DoorLockState newState) {
            if (this.frontDoorPropId <= 0) return;
            if (!ServerPropManager.Instance.TryGetPropState(FrontDoorRoomId, this.frontDoorPropId, out var state)) return;
            DoorState current = DoorState.Deserialize(state.Payload);
            if (current.LockState == newState) return;
            current.LockState = newState;
            ServerPropManager.Instance.UpdatePropState(FrontDoorRoomId, this.frontDoorPropId, current.Serialize());
        }

        // Front door lives in the city room so all players (hall + apt) see it.
        private const string FrontDoorRoomId = "city";

        /// <summary>PropId of the apartment's front door (new system). 0 if not yet spawned.</summary>
        public int FrontDoorPropId => frontDoorPropId;
        private int frontDoorPropId;

        [Header("Prop Config IDs (PropsConfig.GetId())")]
        [SerializeField, Tooltip("PropsConfig id of the front door prefab. Must be in PropsDatabase.")]
        private int frontDoorPrefabConfigId;

        [SerializeField, Tooltip("PropsConfig id of the inner (simple) door prefab. Must be in PropsDatabase.")]
        private int simpleDoorPrefabConfigId;

        [Server]
        private IEnumerator RetrieveData() {
            UnityWebRequest request = ApiManager.Instance.RetrieveHomeRequest(this.address);

            yield return request.SendWebRequest();

            Home homeResponse = JsonUtility.FromJson<Home>(request.downloadHandler.text);

            if (homeResponse?.Id != null) {
                this.homeData = homeResponse;
                this.presetName = this.homeData.Preset;
                
                UnityWebRequest tenantRequest = ApiManager.Instance.RetrieveCharacterByIdRequest(this.HomeData.Tenant);

                yield return tenantRequest.SendWebRequest();

                CharacterResponse characterResponse = JsonUtility.FromJson<CharacterResponse>(tenantRequest.downloadHandler.text);

                if (characterResponse?.Characters?.Length > 0) {
                    this.tenant = characterResponse.Characters[0];
                    this.tenantId = this.tenant.Id;
                    this.tenantIdentity = this.tenant.Identity;
                } else {
                    Debug.LogError($"[ApartmentController] [RetrieveData] Cannot retrieve tenant data with [tenantId={this.HomeData.Tenant}, homeId={this.HomeData.Id}]");
                }

                if (this.presetName == "ahmed") {
                    this.currentConfiguration = this.ahmedConfiguration;
                } else if (this.presetName == "talyah") {
                    this.currentConfiguration = this.talyahConfiguration;
                } else {
                    this.currentConfiguration = this.katarinaConfiguration;
                }

                InstantiateLevel(homeResponse.SceneData);

                this.SetFrontDoorLockState(DoorLockState.UNLOCKED);
            } else {
                this.SetFrontDoorLockState(DoorLockState.LOCKED);
                this.state = ApartmentState.NOT_GENERATED;
                this.associatedHallController.CheckGenerationState();
            }
        }

        [Server]
        private void InstantiateLevel(SceneData sceneData) {
            // Spawn paint buckets in the new prop system (PaintBucketState carries paintConfigId+color)
            sceneData.buckets?.ToList().ForEach(data => {
                SaveUtils.SpawnPropFromSave(data, this);
            });

            // Spawn standard props in the new prop system
            sceneData.props?.ToList().ForEach(data => {
                SaveUtils.SpawnPropFromSave(data, this);
            });

            if (sceneData.walls != null) {
                Dictionary<int, CoverSettings> wallSettings = sceneData.walls.ToDictionary(
                    x => x.idx,
                    x => new CoverSettings { paintConfigId = x.paintConfigId, additionalColor = x.GetColor() }
                );

                for (int i = 0; i < this.currentConfiguration.walls.SharedMaterials().Length; i++) {
                    if (wallSettings.ContainsKey(i)) {
                        this.coverSettingsByFaces.Add(i, wallSettings[i]);
                    } else {
                        this.coverSettingsByFaces.Add(i, defaultWallCoverSettings);
                    }
                }
            } else {
                for (int i = 0; i < this.currentConfiguration.walls.SharedMaterials().Length; i++) {
                    this.coverSettingsByFaces.Add(i, defaultWallCoverSettings);
                }
            }

            if (sceneData.grounds != null) {
                Dictionary<int, CoverSettings> groundSettings = sceneData.grounds.ToDictionary(
                    x => x.idx,
                    x => new CoverSettings { paintConfigId = x.paintConfigId, additionalColor = x.GetColor() }
                );

                for (int i = 0; i < this.grounds.Length; i++) {
                    if (groundSettings.ContainsKey(i)) {
                        this.coverSettingsByGround.Add(i, groundSettings[i]);
                    } else {
                        this.coverSettingsByGround.Add(i, defaultGroundCoverSettings);
                    }
                }
            } else {
                for (int i = 0; i < this.grounds.Length; i++) {
                    this.coverSettingsByGround.Add(i, defaultGroundCoverSettings);
                }
            }

            // Inner doors live in the apartment room (visible only to people inside).
            // Default lock state is UNLOCKED (set by DoorPropSource serialized field).
            foreach (var spawner in this.currentConfiguration.doorSpawners) {
                int innerDoorPropId = ServerPropManager.Instance.SpawnProp(
                    this.RoomId,
                    this.simpleDoorPrefabConfigId,
                    spawner.position,
                    spawner.rotation
                );
                if (innerDoorPropId >= 0) {
                    GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(innerDoorPropId);
                    if (go != null) {
                        go.transform.SetParent(this.transform);
                        go.transform.position = spawner.position;
                        go.transform.rotation = spawner.rotation;
                    }
                }
            }

            // Spawn the apartment delivery box via the new prop system.
            // The prefab must carry: PropIdentity, DeliveryBoxBehaviour, DeliveryBoxPropSource.
            int deliveryBoxConfigId = this.deliveryBoxPrefabConfigId;
            int deliveryBoxPropId = ServerPropManager.Instance.SpawnProp(
                this.RoomId,
                deliveryBoxConfigId,
                this.currentConfiguration.deliveryBoxSpawn.position,
                this.currentConfiguration.deliveryBoxSpawn.rotation
            );
            if (deliveryBoxPropId >= 0) {
                GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(deliveryBoxPropId);
                if (go != null) {
                    go.transform.SetParent(this.propsContainer);
                    go.transform.position = this.currentConfiguration.deliveryBoxSpawn.position;
                    go.transform.rotation = this.currentConfiguration.deliveryBoxSpawn.rotation;
                }
                this.deliveryBoxPropId = deliveryBoxPropId;

                // Trigger initial fetch of the deliveries to populate the count.
                if (!string.IsNullOrEmpty(this.tenantId)) {
                    PropInteractionDispatcher.Instance?.RefreshDeliveryBoxCount(deliveryBoxPropId, this.RoomId, this.tenantId);
                }
            }

            this.SetFrontDoorLockState(DoorLockState.UNLOCKED);

            this.state = ApartmentState.GENERATED;

            this.associatedHallController.CheckGenerationState();
        }

        /// <summary>PropId of the apartment's delivery box (new system). 0 if not yet spawned.</summary>
        public int DeliveryBoxPropId => deliveryBoxPropId;
        private int deliveryBoxPropId;

        [SerializeField, Tooltip("PropsConfig id (PropsConfig.GetId()) of the delivery box prefab. Must be in PropsDatabase.")]
        private int deliveryBoxPrefabConfigId;

        public void ResetWallPreview() {
            this.currentConfiguration.walls.Reset();
        }

        public void ResetGroundPreview() {
            this.grounds.Where(x => x.IsPreview()).ToList().ForEach(x => x.ResetPreview());
        }

        public void ApplyWallSettings() {
            this.CmdApplyWallSettings(SaveUtils.CreateCoverDatas(this.currentConfiguration.walls.CoverSettingsInPreview));
            this.currentConfiguration.walls.ApplyModification();
        }

        public void ApplyGroundSettings() {
            Ground[] groundFiltered = this.grounds.Where(x => x.IsPreview()).ToArray();
            Dictionary<int, CoverSettings> groundDataToUpdate = groundFiltered.ToDictionary(x => Array.IndexOf(grounds, x), x => x.CurrentCover);

            foreach (var ground in groundFiltered) {
                ground.ApplyModification();
            }

            this.CmdApplyGroundSettings(SaveUtils.CreateCoverDatas(groundDataToUpdate));
        }

        [Command(requiresAuthority = false)]
        public void CmdApplyWallSettings(CoverData[] newSettings, NetworkConnectionToClient sender = null) {
            foreach (var wallFaceData in newSettings) {
                this.coverSettingsByFaces[wallFaceData.idx] = new CoverSettings {
                    paintConfigId = wallFaceData.paintConfigId,
                    additionalColor = wallFaceData.GetColor()
                };
            }

            Debug.Log("Server: Apply wall settings");
            StartCoroutine(this.Save());
        }

        [Command(requiresAuthority = false)]
        public void CmdApplyGroundSettings(CoverData[] newSettings, NetworkConnectionToClient sender = null) {
            foreach (var groundData in newSettings) {
                this.coverSettingsByGround[groundData.idx] = new CoverSettings {
                    paintConfigId = groundData.paintConfigId,
                    additionalColor = groundData.GetColor()
                };
            }

            Debug.Log("Server: Apply ground settings");
            StartCoroutine(this.Save());
        }

        [Server]
        private SceneData GenerateSceneData() {
            // Read all props in this apartment's room from the new prop system,
            // skipping the delivery box (lifecycle managed by the apartment, not saved).
            List<BucketData>  buckets = new List<BucketData>();
            List<DefaultData> props   = new List<DefaultData>();

            foreach (ServerPropState state in ServerPropManager.Instance.GetRoomStates(this.RoomId)) {
                if (state.IsScene) continue;                       // scene props not saved
                if (state.PropId == this.deliveryBoxPropId) continue; // delivery box re-spawned on load

                if (state.Type == PropType.PaintBucket) {
                    buckets.Add(new BucketData(state, this.propsContainer));
                } else {
                    props.Add(new DefaultData(state, this.propsContainer));
                }
            }

            SceneData sceneData = new SceneData {
                walls   = SaveUtils.CreateCoverDatas(this.coverSettingsByFaces),
                grounds = SaveUtils.CreateCoverDatas(this.coverSettingsByGround),
                buckets = buckets.ToArray(),
                props   = props.ToArray()
            };

            return sceneData;
        }

        public ApartmentState State => state;

        public Home HomeData {
            get => homeData;
            set => homeData = value;
        }

        public string TenantId => tenantId;

        public Identity TenantIdentity => tenantIdentity;

        public bool IsTenant(CharacterData character) {
            return character.Id == this.tenantId;
        }
        
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