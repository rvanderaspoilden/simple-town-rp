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

namespace Sim {
    public class NetworkManager : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField]
        private Character characterPrefab;

        [Header("Only for debug")]
        [SerializeField]
        private CharacterData characterData;

        [SerializeField]
        private List<Home> characterHomes;

        [SerializeField]
        private Home tenantHome;

        private string destinationScene;

        private int currentAppartmentNumber;

        public static NetworkManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        private void OnDestroy() {
            StopAllCoroutines();
        }

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

        public void Play() {
            LoadingManager.Instance.Show(true);

            PhotonNetwork.NickName = this.characterData.Identity.FullName;

            this.ConnectToMasterServer();
        }

        public Character CharacterPrefab {
            get => characterPrefab;
            set => characterPrefab = value;
        }

        public void GoToRoom(PlacesEnum place) {
            // If player is alreay in a room so leave it and join another
            if (PhotonNetwork.InRoom) {
                StartCoroutine(this.LeaveAndJoinRoom(place));
                return;
            }

            string roomName = PlaceUtils.GetPlaceEnumName(place);

            if (place == PlacesEnum.HALL) {
                roomName =
                    $"{PlaceUtils.GetPlaceEnumName(place)} n°{CommonUtils.GetAppartmentFloorFromAppartmentId(tenantHome.Address.DoorNumber, CommonConstants.appartmentLimitPerFloor)}";
            } else if (place == PlacesEnum.HOME) {
                roomName = $"{PlaceUtils.GetPlaceEnumName(place)} n°{this.currentAppartmentNumber}"; // Todo use address
            }

            PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions() {IsOpen = true, IsVisible = true, EmptyRoomTtl = 0}, TypedLobby.Default);
            this.destinationScene = PlaceUtils.ConvertPlaceEnumToSceneName(place);
        }

        public void GoToHome(int id) {
            this.currentAppartmentNumber = id;
            this.GoToRoom(PlacesEnum.HOME);
        }

        private IEnumerator LeaveAndJoinRoom(PlacesEnum place) {
            LoadingManager.Instance.Show();

            yield return new WaitForSeconds(1f);

            PhotonNetwork.LeaveRoom();

            // Wait reconnect to master server before join a room
            while (!PhotonNetwork.InLobby) {
                yield return null;
            }

            if (PhotonNetwork.IsConnectedAndReady) {
                this.GoToRoom(place);
            }
        }

        private void ConnectToMasterServer() {
            PhotonNetwork.ConnectUsingSettings();
        }

        private IEnumerator LoadScene(string sceneName) {
            PhotonNetwork.LoadLevel(sceneName);

            UnityWebRequest webRequest = null;

            if (sceneName.Equals(PlaceUtils.ConvertPlaceEnumToSceneName(PlacesEnum.HOME))) {
                // TODO: retrieve by address
                webRequest = ApiManager.instance.RetrieveHomeById(this.currentAppartmentNumber);
            }

            while (PhotonNetwork.LevelLoadingProgress < 1f || (webRequest != null && !webRequest.isDone)) {
                Debug.Log("Room loading...");
                yield return new WaitForEndOfFrame();
            }

            Debug.Log("Room is loaded");

            if (PhotonNetwork.IsMasterClient && sceneName.Equals(Scenes.HALL)) { // TODO use preset database
                TextAsset textAsset = Resources.Load<TextAsset>("PresetSceneDatas/Hall");
                SceneData sceneData = JsonUtility.FromJson<SceneData>(textAsset.text);
                RoomManager.Instance.InstantiateLevel(sceneData);
            }

            if (sceneName.Equals(Scenes.HOME)) {
                HomeResponse homeResponse = JsonUtility.FromJson<HomeResponse>(webRequest.downloadHandler.text);
                SceneData sceneData = null;

                if (homeResponse != null && homeResponse.Homes.Length > 0) {
                    Home home = homeResponse.Homes[0];
                    // If no data found from API use default appartment to prevent crash
                    sceneData = home.SceneData;
                    AppartmentManager.instance.SetAppartmentData(home.Owner, home.Id);
                } else {
                    TextAsset textAsset = Resources.Load<TextAsset>("PresetSceneDatas/Default_Appartment_Talyah");
                    sceneData = JsonUtility.FromJson<SceneData>(textAsset.text);
                    AppartmentManager.instance.SetAppartmentData(null, this.currentAppartmentNumber.ToString());
                }

                if (PhotonNetwork.IsMasterClient) {
                    RoomManager.Instance.InstantiateLevel(sceneData);
                }
            }

            bool isRoomGenerated;
            do {
                isRoomGenerated = PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("isGenerated") && RoomManager.Instance.IsGenerated();

                if (isRoomGenerated) {
                    RoomManager.Instance.InstantiateLocalCharacter(this.characterPrefab, this.characterData);
                }

                yield return new WaitForSeconds(0.1f);
            } while (!isRoomGenerated);

            yield return new WaitForSeconds(0.5f);
            LoadingManager.Instance.Hide();
        }

        #region Callbacks

        public override void OnConnectedToMaster() {
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }

        public override void OnJoinedLobby() {
            Debug.Log("Lobby joined");
            Debug.Log("Connecting to server with character : " + characterData.Identity.Firstname);
            this.GoToRoom(PlacesEnum.HALL);
        }

        public override void OnJoinedRoom() {
            Debug.Log("I joined room : " + PhotonNetwork.CurrentRoom.Name);
            StartCoroutine(this.LoadScene(this.destinationScene));
        }

        public override void OnJoinRoomFailed(short returnCode, string message) {
            base.OnJoinRoomFailed(returnCode, message);

            LoadingManager.Instance.Hide();
        }

        #endregion
    }
}