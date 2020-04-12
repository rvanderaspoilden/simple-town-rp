using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using Sim.Constants;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class NetworkManager : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField] private GameObject playerPrefab;
        
        [Header("Only for debug")]
        [SerializeField] private Personnage personnage;

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

        public void Play(Personnage personnage) {
            this.personnage = personnage;

            if (PhotonNetwork.IsConnectedAndReady) {
                Debug.Log("Connecting to server with personnage : " + personnage.GetFirstname());

                PhotonNetwork.JoinOrCreateRoom(Places.TOWN_SQUARE, new RoomOptions() {IsOpen = true, IsVisible = true, EmptyRoomTtl = 10000}, TypedLobby.Default);
            } else {
                Debug.Log("Player is not connected to lobby");
            }
        }

        private void ConnectToMasterServer() {
            PhotonNetwork.ConnectUsingSettings();
        }

        private IEnumerator LoadRoom(string roomName) {
            PhotonNetwork.LoadLevel(roomName);

            while (PhotonNetwork.LevelLoadingProgress < 1f) {
                Debug.Log("Room loading...");
                yield return new WaitForEndOfFrame();
            }
            
            Debug.Log("Room is loaded");
            RoomManager.Instance.InstantiateLocalPlayer(this.playerPrefab, this.personnage);
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
            StartCoroutine(this.LoadRoom(PhotonNetwork.CurrentRoom.Name));
        }

        #endregion
    }
}