using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using Sim.Constants;
using Sim.Entities;
using Sim.Enums;
using Sim.Utils;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Sim {
    public class NetworkManagerOld : MonoBehaviourPunCallbacks {
        [FormerlySerializedAs("characterPrefab")]
        [Header("Settings")]
        [SerializeField]
        private PlayerController playerPrefab;

        [SerializeField]
        private bool testEntrance;

        [Header("Only for debug")]
        [SerializeField]
        private CharacterData characterData;

        [SerializeField]
        private List<Home> characterHomes;

        [SerializeField]
        private Home tenantHome;

        [SerializeField]
        private RoomNavigationData oldRoomData;

        [SerializeField]
        private RoomNavigationData currentRoomData;

        [SerializeField]
        private RoomNavigationData nextRoomData;

        public static NetworkManagerOld Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }

            DontDestroyOnLoad(this.gameObject);
        }

        private void OnDestroy() {
            StopAllCoroutines();
        }

        #region GETTER / SETTER

        public CharacterData CharacterData {
            get => characterData;
            set => characterData = value;
        }

        public Home TenantHome {
            get => tenantHome;
            set => tenantHome = value;
        }

        public List<Home> CharacterHomes {
            get => characterHomes;
            set {
                characterHomes = value;
                TenantHome = characterHomes.Find(x => x.Tenant == this.characterData.Id);
            }
        }

        public PlayerController PlayerPrefab {
            get => playerPrefab;
            set => playerPrefab = value;
        }

        #endregion

        public void Play() {
            LoadingManager.Instance.Show(true);

            PhotonNetwork.NickName = this.characterData.Identity.FullName;

            this.nextRoomData = new RoomNavigationData(this.testEntrance ? RoomTypeEnum.ENTRANCE : RoomTypeEnum.HOME, tenantHome.Address);

            PhotonNetwork.ConnectUsingSettings();
        }

        public void GoToRoom(RoomTypeEnum roomType, Address address) {
            LoadingManager.Instance.Show(true);

            // If player is already in a room so leave it to rejoin
            if (PhotonNetwork.InRoom) {
                this.nextRoomData = new RoomNavigationData(roomType, address);

                PhotonNetwork.LeaveRoom();
                return;
            }

            string roomName = CommonUtils.GetSceneName(roomType);

            if (roomType.Equals(RoomTypeEnum.BUILDING_HALL)) {
                int doorNumber = this.currentRoomData != null && this.currentRoomData.RoomType == RoomTypeEnum.HOME
                    ? this.currentRoomData.Address.DoorNumber
                    : this.tenantHome.Address.DoorNumber;

                roomName = $"Floor {CommonUtils.GetApartmentFloor(doorNumber, CommonConstants.appartmentLimitPerFloor)}, SALMON HOTEL";
            } else if (roomType.Equals(RoomTypeEnum.HOME)) {
                roomName = address.ToString();
            }

            PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions() {IsOpen = true, IsVisible = true, EmptyRoomTtl = 0}, TypedLobby.Default);
        }

        private IEnumerator LoadScene(string sceneName) {
            PhotonNetwork.LoadLevel(sceneName);

            UnityWebRequest webRequest = null;

            if (sceneName.Equals(CommonUtils.GetSceneName(RoomTypeEnum.HOME))) {
                webRequest = ApiManager.Instance.RetrieveHomeByAddress(this.nextRoomData.Address);
            }

            while (PhotonNetwork.LevelLoadingProgress < 1f || (webRequest != null && !webRequest.isDone)) {
                Debug.Log("Room loading...");
                yield return new WaitForEndOfFrame();
            }

            Debug.Log("Room is loaded");

            // Set navigation state
            this.oldRoomData = new RoomNavigationData(this.currentRoomData.RoomType, this.currentRoomData.Address);
            this.currentRoomData = new RoomNavigationData(this.nextRoomData.RoomType, this.nextRoomData.Address);
            this.nextRoomData = null;

            if (this.testEntrance) {
                //RoomManager.Instance.InstantiateLocalCharacter(this.characterPrefab, this.characterData, this.currentRoomData, this.oldRoomData);
                yield return new WaitForSeconds(0.5f);
                LoadingManager.Instance.Hide();
            } else {
                if (PhotonNetwork.IsMasterClient && sceneName.Equals(SceneConstants.HALL)) {
                    // TODO use preset database
                    TextAsset textAsset = Resources.Load<TextAsset>("PresetSceneDatas/Hall");
                    SceneData sceneData = JsonUtility.FromJson<SceneData>(textAsset.text);
                    //RoomManager.Instance.InstantiateLevel(sceneData, currentRoomData, oldRoomData);
                }

                if (sceneName.Equals(SceneConstants.HOME)) {
                    Home homeResponse = JsonUtility.FromJson<Home>(webRequest.downloadHandler.text);
                    SceneData sceneData = null;

                    if (homeResponse != null) {
                        Home home = homeResponse;
                        // If no data found from API use default apartment to prevent crash
                        sceneData = home.SceneData;
                        ApartmentManager.Instance.HomeData = home;
                    } else {
                        // TODO prevent to go in 
                        TextAsset textAsset = Resources.Load<TextAsset>("PresetSceneDatas/Default_Appartment_Talyah");
                        sceneData = JsonUtility.FromJson<SceneData>(textAsset.text);
                        ApartmentManager.Instance.HomeData = null;
                    }

                    if (PhotonNetwork.IsMasterClient) {
                        //RoomManager.Instance.InstantiateLevel(sceneData, currentRoomData, oldRoomData);
                    }
                }

                bool isRoomGenerated;
                do {
                    isRoomGenerated = PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("isGenerated") && RoomManager.Instance.IsGenerated();

                    if (isRoomGenerated) {
                        //RoomManager.Instance.InstantiateLocalCharacter(this.characterPrefab, this.characterData, this.currentRoomData, this.oldRoomData);
                    }

                    yield return new WaitForSeconds(0.1f);
                } while (!isRoomGenerated);

                yield return new WaitForSeconds(0.5f);
                LoadingManager.Instance.Hide();
            }
        }

        #region Callbacks

        public override void OnConnectedToMaster() {
            Debug.Log("On connected to master");

            this.GoToRoom(this.nextRoomData.RoomType, this.nextRoomData.Address);
        }

        public override void OnJoinedRoom() {
            Debug.Log("I joined room : " + PhotonNetwork.CurrentRoom.Name);
            StartCoroutine(this.LoadScene(CommonUtils.GetSceneName(this.nextRoomData.RoomType)));
        }

        public override void OnJoinRoomFailed(short returnCode, string message) {
            base.OnJoinRoomFailed(returnCode, message);

            Debug.LogError("On join room failed => DISCONNECT PLAYER");

            PhotonNetwork.Disconnect();

            LoadingManager.Instance.Hide();
        }

        public override void OnDisconnected(DisconnectCause cause) {
            base.OnDisconnected(cause);

            Debug.Log("I have been disconnected from master server so redirect to main menu");

            SceneManager.LoadScene("Main Menu");
        }

        #endregion
    }
}