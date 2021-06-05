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
    public class ApartmentController : NetworkBehaviour {
        [Header("Settings")]
        [SerializeField]
        private FrontDoor frontDoorPrefab;

        [SerializeField]
        private Transform frontDoorSpawn;

        [SerializeField]
        private SimpleDoor simpleDoorPrefab;

        [SerializeField]
        private DeliveryBox deliveryBoxPrefab;

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
        private FrontDoor frontDoor;

        [SyncVar]
        private uint parentId;

        [SyncVar(hook = nameof(OnSetAddress))]
        [SerializeField]
        private Address address;

        [SyncVar]
        [SerializeField]
        private string tenantId;

        [SyncVar(hook = nameof(OnSetPresetName))]
        [SerializeField]
        private string presetName;

        private ApartmentPresetConfiguration currentConfiguration;

        [SerializeField]
        private HallController associatedHallController;
        
        [SerializeField]
        private ApartmentState state = ApartmentState.NOT_CREATED;

        private Type[] defaultPropsTypes = new[] {typeof(Props), typeof(Seat)};

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

        public override void OnStartClient() {
            base.OnStartClient();
            
            AssignParent();
        }

        private void OnSetAddress(Address old, Address newValue) {
            this.address = newValue;
            
            Debug.Log("OnSetAddress");

            this.geographicArea.LocationText = $"{this.address.street}, Floor {NetworkIdentity.spawned[this.parentId].GetComponent<HallController>().FloorNumber}, Door {this.address.doorNumber}";
        }

        public Address Address => address;

        public Transform SpawnPosition => spawnPosition;

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

            Debug.Log($"OnSetPresetName of {this.name} with presetName : {this.presetName}");
            
            this.coverSettingsByFaces.Callback += OnWallSettingsChanged;
            this.coverSettingsByGround.Callback += OnGroundSettingsChanged;

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
            GetComponentsInChildren<Props>().ToList().Select(x => x.GetComponent<PropsRenderer>()).ToList().ForEach(
                propsRenderer => {
                    if (propsRenderer && propsRenderer.IsHideable()) {
                        propsRenderer.SetVisibilityMode(mode);
                    }
                });

            OnPropsVisibilityModeChanged?.Invoke(mode);
        }

        private void AssignParent() {
            if (parentId == 0) return;

            Vector3 position = this.transform.position;

            if (!isClientOnly) return;

            if (NetworkIdentity.spawned.ContainsKey(this.parentId)) {
                this.associatedHallController = NetworkIdentity.spawned[this.parentId].GetComponent<HallController>();
                this.transform.SetParent(this.associatedHallController.transform);
                this.transform.localPosition = position;

                Debug.Log("Assign parent");
            } else {
                Debug.LogError($"Parent identity not found for appartment {this.name}");
            }
        }

        public override void OnStopClient() {
            base.OnStopClient();

            this.coverSettingsByFaces.Callback -= OnWallSettingsChanged;
            this.coverSettingsByGround.Callback -= OnGroundSettingsChanged;
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

            this.frontDoor = Instantiate(this.frontDoorPrefab, this.frontDoorSpawn.position, this.frontDoorSpawn.rotation);
            this.frontDoor.transform.SetParent(this.propsContainer);
            this.frontDoor.ParentId = netId;
            this.frontDoor.Number = newAddress.doorNumber;
            NetworkServer.Spawn(this.frontDoor.gameObject);

            StartCoroutine(RetrieveData());
        }

        [Server]
        public void Regenerate() {
            StartCoroutine(RetrieveData());
        }

        [Server]
        private IEnumerator RetrieveData() {
            UnityWebRequest request = ApiManager.Instance.RetrieveHomeRequest(this.address);

            yield return request.SendWebRequest();

            Home homeResponse = JsonUtility.FromJson<Home>(request.downloadHandler.text);

            if (homeResponse?.Id != null) {
                Debug.Log($"Home found for Address {address}");
                this.homeData = homeResponse;
                this.tenantId = this.homeData.Tenant;
                this.presetName = this.homeData.Preset;

                if (this.presetName == "ahmed") {
                    this.currentConfiguration = this.ahmedConfiguration;
                } else if (this.presetName == "talyah") {
                    this.currentConfiguration = this.talyahConfiguration;
                } else {
                    this.currentConfiguration = this.katarinaConfiguration;
                }

                InstantiateLevel(homeResponse.SceneData);
                
                this.frontDoor.SetLockState(DoorLockState.UNLOCKED);
            } else {
                Debug.Log($"No Home found for Address {address}");
                this.frontDoor.SetLockState(DoorLockState.LOCKED);
                this.state = ApartmentState.NOT_GENERATED;
                this.associatedHallController.CheckGenerationState();
            }
        }

        [Server]
        private void InstantiateLevel(SceneData sceneData) {
            sceneData.buckets?.ToList().ForEach(data => {
                PaintBucket props = SaveUtils.InstantiatePropsFromSave(data, this) as PaintBucket;
                props.Init(data.paintConfigId, data.color);
            });

            sceneData.props?.ToList().ForEach(data => {
                Props props = SaveUtils.InstantiatePropsFromSave(data, this);
            });

            if (sceneData.walls != null) {
                Dictionary<int, CoverSettings> wallSettings = sceneData.walls.ToDictionary(
                    x => x.idx,
                    x => new CoverSettings {paintConfigId = x.paintConfigId, additionalColor = x.GetColor()}
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
                    x => new CoverSettings {paintConfigId = x.paintConfigId, additionalColor = x.GetColor()}
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

            foreach (var currentConfigurationDoorSpawner in this.currentConfiguration.doorSpawners) {
                SimpleDoor simpleDoor = Instantiate(this.simpleDoorPrefab, currentConfigurationDoorSpawner.position, currentConfigurationDoorSpawner.rotation);
                simpleDoor.ParentId = netId;
                simpleDoor.transform.SetParent(this.transform);
                NetworkServer.Spawn(simpleDoor.gameObject);
            }

            DeliveryBox deliveryBox = Instantiate(this.deliveryBoxPrefab, this.currentConfiguration.deliveryBoxSpawn.position,
                this.currentConfiguration.deliveryBoxSpawn.rotation);
            deliveryBox.ParentId = netId;
            deliveryBox.transform.SetParent(this.transform);
            NetworkServer.Spawn(deliveryBox.gameObject);

            this.frontDoor.SetLockState(DoorLockState.UNLOCKED);

            this.state = ApartmentState.GENERATED;

            this.associatedHallController.CheckGenerationState();
        }

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
            SceneData sceneData = new SceneData {
                walls = SaveUtils.CreateCoverDatas(this.coverSettingsByFaces),
                grounds = SaveUtils.CreateCoverDatas(this.coverSettingsByGround),
                buckets = GetComponentsInChildren<PaintBucket>().ToList().Where(x => x.ApartmentController == this).Select(SaveUtils.CreateBucketData).ToArray(),
                props = GetComponentsInChildren<Props>().ToList()
                    .Where(props => props.ApartmentController == this && defaultPropsTypes.Contains(props.GetType()))
                    .Select(SaveUtils.CreateDefaultData).ToArray()
            };

            return sceneData;
        }

        public ApartmentState State => state;

        public Home HomeData {
            get => homeData;
            set => homeData = value;
        }

        public bool IsTenant(CharacterData character) {
            return character.Id == this.tenantId;
        }

        public uint ParentId {
            get => parentId;
            set => parentId = value;
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