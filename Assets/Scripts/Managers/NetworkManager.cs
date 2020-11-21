using System.Collections;
using Newtonsoft.Json;
using Photon.Pun;
using Photon.Realtime;
using Sim.Entities;
using Sim.Enums;
using Sim.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim {
    public class NetworkManager : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField]
        private GameObject playerPrefab;

        [Header("Only for debug")]
        [SerializeField]
        private Personnage personnage;

        private string destinationScene;

        private int currentAppartmentNumber;

        private bool isConnectedToServer;

        public static NetworkManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        private void Start() {
            this.ConnectToMasterServer();
        }

        private void OnDestroy() {
            StopAllCoroutines();
        }

        public Personnage Personnage => personnage;

        public void Play(Personnage personnage) {
            this.personnage = personnage;

            if (PhotonNetwork.IsConnectedAndReady && this.isConnectedToServer) {
                LoadingManager.Instance.Show();
                Debug.Log("Connecting to server with personnage : " + personnage.Firstname);
                this.GoToRoom(PlacesEnum.HALL);
            } else {
                Debug.Log("Player is not connected to lobby");
            }
        }

        public GameObject PlayerPrefab {
            get => playerPrefab;
            set => playerPrefab = value;
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
                    $"{PlaceUtils.GetPlaceEnumName(place)} n°{CommonUtils.GetAppartmentFloorFromAppartmentId(personnage.AppartmentId, CommonConstants.appartmentLimitPerFloor)}";
            } else if (place == PlacesEnum.APPARTMENT) {
                roomName = $"{PlaceUtils.GetPlaceEnumName(place)} n°{this.currentAppartmentNumber}";
            }

            PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions() {IsOpen = true, IsVisible = true, EmptyRoomTtl = 0}, TypedLobby.Default);
            this.destinationScene = PlaceUtils.ConvertPlaceEnumToSceneName(place);
        }

        public void GoToAppartment(int id) {
            this.currentAppartmentNumber = id;
            this.GoToRoom(PlacesEnum.APPARTMENT);
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

            if (sceneName.Equals(PlaceUtils.ConvertPlaceEnumToSceneName(PlacesEnum.APPARTMENT))) {
                webRequest = ApiManager.instance.RetrieveAppartment(this.currentAppartmentNumber);
            }

            while (PhotonNetwork.LevelLoadingProgress < 1f || (webRequest != null && !webRequest.isDone)) {
                Debug.Log("Room loading...");
                yield return new WaitForEndOfFrame();
            }

            Debug.Log("Room is loaded");

            if (PhotonNetwork.IsMasterClient && sceneName.Equals("Hall")) {
                TextAsset textAsset = Resources.Load<TextAsset>("PresetSceneDatas/Hall");
                SceneData sceneData = JsonConvert.DeserializeObject<SceneData>(textAsset.text);
                RoomManager.Instance.InstantiateLevel(sceneData);
            }

            if (sceneName.Equals("Appartment")) {
                AppartmentResponse appartmentResponse = JsonUtility.FromJson<AppartmentResponse>(webRequest.downloadHandler.text);
                SceneData sceneData = null;

                if (appartmentResponse != null) {
                    // If no data found from API use default appartment to prevent crash
                    sceneData = appartmentResponse.GetData();
                    AppartmentManager.instance.SetAppartmentData(appartmentResponse.GetOwner(), appartmentResponse.GetUid());
                } else {
                    TextAsset textAsset = Resources.Load<TextAsset>("PresetSceneDatas/Default_Appartment_Talyah");
                    sceneData = JsonUtility.FromJson<SceneData>(textAsset.text);
                    AppartmentManager.instance.SetAppartmentData(null, this.currentAppartmentNumber.ToString());
                }

                if (PhotonNetwork.IsMasterClient) {
                    RoomManager.Instance.InstantiateLevel(sceneData);
                }
            }

            /*bool isRoomGenerated;
            do {
                isRoomGenerated = PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("isGenerated");
                
                if (isRoomGenerated) {
                    RoomManager.Instance.InstantiateLocalPlayer(this.playerPrefab, this.personnage);
                }
                
                yield return new WaitForSeconds(0.1f);
            } while (!isRoomGenerated);

            LoadingManager.Instance.Hide();*/
        }

        #region Callbacks

        public override void OnConnectedToMaster() {
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }

        public override void OnJoinedLobby() {
            Debug.Log("Lobby joined");
            this.isConnectedToServer = true;
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