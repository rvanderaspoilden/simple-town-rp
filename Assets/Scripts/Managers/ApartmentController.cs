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
        private FrontDoor frontDoor;

        [SerializeField]
        private Transform originPoint;

        [SerializeField]
        private Transform propsContainer;

        [SerializeField]
        private Wall wall;

        [SerializeField]
        private CoverSettings defaultWallCoverSettings;

        [SerializeField]
        private CoverSettings defaultGroundCoverSettings;

        [SerializeField]
        private Ground[] grounds;

        [Header("Only for debug")]
        [SerializeField]
        private Home homeData;

        [SyncVar(hook = nameof(DoorNumberChanged))]
        [SerializeField]
        private int doorNumber;

        [SerializeField]
        private Address address;

        [SyncVar]
        [SerializeField]
        private string tenantId;

        [SerializeField]
        private bool isGenerated;

        private Type[] defaultPropsTypes = new[] {typeof(Props), typeof(Seat), typeof(DeliveryBox)};

        private readonly SyncDictionary<int, CoverSettings> coverSettingsByFaces = new SyncDictionary<int, CoverSettings>();

        private readonly SyncDictionary<int, CoverSettings> coverSettingsByGround = new SyncDictionary<int, CoverSettings>();

        public delegate void GenerateResponse();

        public event GenerateResponse OnApartmentGenerated;

        [Command(requiresAuthority = false)]
        public void CmdSaveHome(NetworkConnectionToClient sender = null) {
            StartCoroutine(this.Save());
            //this.SaveLocal();
        }

        public override void OnStartServer() {
            base.OnStartServer();

            Address address = new Address {
                Street = "SALMON HOTEL",
                DoorNumber = 1,
                HomeType = HomeTypeEnum.APARTMENT
            };

            this.Init(address);
        }

        public override void OnStartClient() {
            base.OnStartClient();

            this.coverSettingsByFaces.Callback += OnWallSettingsChanged;
            this.coverSettingsByGround.Callback += OnGroundSettingsChanged;

            this.wall.Setup(this.coverSettingsByFaces.ToDictionary(x => x.Key, x => x.Value));

            for (int i = 0; i < this.grounds.Length; i++) {
                if (this.coverSettingsByGround.ContainsKey(i)) {
                    this.grounds[i].PaintConfigId = this.coverSettingsByGround[i].paintConfigId;
                }
            }
        }

        public override void OnStopClient() {
            base.OnStopClient();

            this.coverSettingsByFaces.Callback -= OnWallSettingsChanged;
            this.coverSettingsByGround.Callback -= OnGroundSettingsChanged;
        }

        private void OnWallSettingsChanged(SyncIDictionary<int, CoverSettings>.Operation operation, int key, CoverSettings item) {
            this.wall.Setup(this.coverSettingsByFaces.ToDictionary(x => x.Key, x => x.Value));
        }
        
        private void OnGroundSettingsChanged(SyncIDictionary<int, CoverSettings>.Operation operation, int key, CoverSettings item) {
            this.grounds[key].PaintConfigId = item.paintConfigId;
        }

        public Transform PropsContainer => propsContainer;

        [Client]
        public void DoorNumberChanged(int old, int newValue) {
            this.doorNumber = newValue;
            this.frontDoor.Number = newValue;
        }

        [Server]
        public IEnumerator Save() {
            // TODO: handle save queue

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
        public void Init(Address newAddress) {
            this.address = newAddress;
            this.doorNumber = newAddress.DoorNumber;

            StartCoroutine(RetrieveData());
        }

        [Server]
        private IEnumerator RetrieveData() {
            UnityWebRequest request = ApiManager.Instance.RetrieveHomeRequest(this.address);

            yield return request.SendWebRequest();

            Home homeResponse = JsonUtility.FromJson<Home>(request.downloadHandler.text);

            this.isGenerated = true;

            OnApartmentGenerated?.Invoke();

            if (homeResponse != null) {
                Debug.Log($"Home found for Address {address}");
                this.homeData = homeResponse;
                this.tenantId = this.homeData.Tenant;

                InstantiateLevel(homeResponse.SceneData);
            } else {
                Debug.Log($"No Home found for Address {address}");
            }
        }

        [Server]
        private void InstantiateLevel(SceneData sceneData) {
            // Instantiate all buckets
            sceneData.buckets?.ToList().ForEach(data => {
                PaintBucket props = SaveUtils.InstantiatePropsFromSave(data, this) as PaintBucket;
                props.Init(data.paintConfigId, data.color);
            });

            // Instantiate all other props
            sceneData.props?.ToList().ForEach(data => {
                Props props = SaveUtils.InstantiatePropsFromSave(data, this);
            });

            if (sceneData.walls != null) {
                Dictionary<int, CoverSettings> wallSettings = sceneData.walls.ToDictionary(
                    x => x.idx,
                    x => new CoverSettings {paintConfigId = x.paintConfigId, additionalColor = x.GetColor()}
                );

                for (int i = 0; i < this.wall.SharedMaterials().Length; i++) {
                    if (wallSettings.ContainsKey(i)) {
                        this.coverSettingsByFaces.Add(i, wallSettings[i]);
                    } else {
                        this.coverSettingsByFaces.Add(i, defaultWallCoverSettings);
                    }
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
            }

            this.isGenerated = true;

            OnApartmentGenerated?.Invoke();
        }

        public void ResetWallPreview() {
            this.wall.Reset();
        }

        public void ResetGroundPreview() {
            this.grounds.Where(x => x.IsPreview()).ToList().ForEach(x => x.ResetPreview());
        }

        public void ApplyWallSettings() {
            this.CmdApplyWallSettings(SaveUtils.CreateCoverDatas(this.wall.CoverSettingsInPreview));
            this.wall.ApplyModification();
        }

        public void ApplyGroundSettings() {
            Ground[] groundFiltered = this.grounds.Where(x => x.IsPreview()).ToArray(); 
            Dictionary<int, CoverSettings> groundDataToUpdate = groundFiltered.ToDictionary(x => Array.IndexOf(grounds, x), x => x.CoverSettings());
            
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
                buckets = FindObjectsOfType<PaintBucket>().ToList().Select(SaveUtils.CreateBucketData).ToArray(),
                props = FindObjectsOfType<Props>().ToList()
                    .Where(props => defaultPropsTypes.Contains(props.GetType()))
                    .Select(SaveUtils.CreateDefaultData).ToArray()
            };

            return sceneData;
        }

        public bool IsGenerated => isGenerated;

        public Home HomeData {
            get => homeData;
            set => homeData = value;
        }

        public bool IsTenant(CharacterData character) {
            return character.Id == this.tenantId;
        }
    }
}