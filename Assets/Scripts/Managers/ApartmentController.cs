using System;
using System.Collections;
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

        public delegate void GenerateResponse();

        public event GenerateResponse OnApartmentGenerated;

        private void Update() {
            /*if (Input.GetKeyDown(KeyCode.S)) {
                CmdSaveHome();
            }*/
        }

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

        public Transform PropsContainer => propsContainer;

        [Client]
        public void DoorNumberChanged(int old, int newValue) {
            this.doorNumber = newValue;
            this.frontDoor.Number = newValue;
        }

        [Server]
        public IEnumerator Save() {
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
            // Instantiate all grounds
            /*sceneData.grounds?.ToList().ForEach(data => {
                Ground props = SaveUtils.InstantiatePropsFromSave(data, originPoint.position) as Ground;
                props.Init(data.paintConfigId);
                props.transform.parent = this.transform;
            });*/

            // Instantiate all walls
            /*sceneData.walls?.ToList().ForEach(data => {
                Wall props = SaveUtils.InstantiatePropsFromSave(data) as Wall;
                props.Init(JsonHelper.ToJson(data.wallFaces));
            });*/

            // Instantiate all simple doors
            // sceneData.simpleDoors?.ToList().ForEach(data => {
            //     Props props = SaveUtils.InstantiatePropsFromSave(data, originPoint.position);
            //     props.transform.parent = this.transform;
            // });

            // Instantiate all buckets
            sceneData.buckets?.ToList().ForEach(data => {
                PaintBucket props = SaveUtils.InstantiatePropsFromSave(data, this) as PaintBucket;
                props.Init(data.paintConfigId, data.color);
            });

            // Instantiate all other props
            sceneData.props?.ToList().ForEach(data => {
                Props props = SaveUtils.InstantiatePropsFromSave(data, this);
            });

            this.isGenerated = true;

            OnApartmentGenerated?.Invoke();
        }

        private SceneData GenerateSceneData() {
            SceneData sceneData = new SceneData {
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