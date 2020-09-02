using System;
using System.Collections;
using Newtonsoft.Json;
using Photon.Pun;
using Photon.Realtime;
using Sim.Constants;
using Sim.Entities;
using Sim.Enums;
using Sim.Utils;
using UnityEngine;

namespace Sim {
    public class NetworkManager : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField] private GameObject playerPrefab;

        [Header("Only for debug")]
        [SerializeField] private Personnage personnage;


        private string destinationScene;

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

        private void Update() {
            if (Input.GetKeyDown(KeyCode.O)) {
                RoomManager.Instance.SaveRoom();
            }
        }

        private void OnDestroy() {
            StopAllCoroutines();
        }

        public void Play(Personnage personnage) {
            this.personnage = personnage;

            if (PhotonNetwork.IsConnectedAndReady) {
                LoadingManager.Instance.Show();
                Debug.Log("Connecting to server with personnage : " + personnage.GetFirstname());
                this.GoToRoom(PlacesEnum.HALL);
            } else {
                Debug.Log("Player is not connected to lobby");
            }
        }

        public void GoToRoom(PlacesEnum place) {
            // If player is alreay in a room so leave it and join another
            if (PhotonNetwork.InRoom) {
                StartCoroutine(this.LeaveAndJoinRoom(place));
                return;
            }

            PhotonNetwork.JoinOrCreateRoom(PlaceUtils.GetPlaceEnumName(place), new RoomOptions() {IsOpen = true, IsVisible = true, EmptyRoomTtl = 10000}, TypedLobby.Default);
            this.destinationScene = PlaceUtils.ConvertPlaceEnumToSceneName(place);
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

            while (PhotonNetwork.LevelLoadingProgress < 1f) {
                Debug.Log("Room loading...");
                yield return new WaitForEndOfFrame();
            }

            Debug.Log("Room is loaded");

            if (PhotonNetwork.IsMasterClient) {
                if (sceneName.Equals("Hall")) {
                    TextAsset hallSceneData = Resources.Load<TextAsset>("PresetSceneDatas/Hall");
                    SceneData sceneData = JsonConvert.DeserializeObject<SceneData>(hallSceneData.text);
                    RoomManager.Instance.InstantiateLevel(sceneData);
                }
            }

            RoomManager.Instance.InstantiateLocalPlayer(this.playerPrefab, this.personnage);

            LoadingManager.Instance.Hide();
        }

        #region Callbacks

        public override void OnConnectedToMaster() {
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }

        public override void OnJoinedLobby() {
            Debug.Log("Lobby joined");
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