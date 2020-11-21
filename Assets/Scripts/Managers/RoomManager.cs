using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Photon.Pun;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Interactables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Sim {
    public class RoomManager : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField]
        protected Transform playerSpawnPoint;

        private readonly WaitForSeconds saveDelay = new WaitForSeconds(1);
        private Coroutine saveCoroutine;

        public static Player LocalPlayer;

        public static RoomManager Instance;

        protected virtual void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
        }

        #region Level generation

        public void InstantiateLevel(SceneData sceneData) {
            // Instantiate all grounds
            sceneData.grounds?.ToList().ForEach(data => {
                Ground props = SaveUtils.InstantiatePropsFromSave(data) as Ground;
                props.SetPaintConfigId(data.paintConfigId, RpcTarget.All);
            });

            // Instantiate all walls
            sceneData.walls?.ToList().ForEach(data => {
                Wall props = SaveUtils.InstantiatePropsFromSave(data) as Wall;
                props.SetWallFaces(data.wallFaces.Select(faceData => faceData.ToWallFace()).ToList(), RpcTarget.All);
            });

            // Instantiate all doors teleporter
            sceneData.doorTeleporters?.ToList().ForEach(data => {
                DoorTeleporter props = SaveUtils.InstantiatePropsFromSave(data) as DoorTeleporter;
                props.SetDestination((PlacesEnum) Enum.Parse(typeof(PlacesEnum), data.destination), RpcTarget.All);
                props.SetDoorDirection((DoorDirectionEnum) Enum.Parse(typeof(DoorDirectionEnum), data.doorDirection), RpcTarget.All);
                props.SetDoorNumber(CommonUtils.GetDoorNumberFromFloorNumber(data.number), RpcTarget.All);
            });

            // Instantiate all simple doors
            sceneData.simpleDoors?.ToList().ForEach(data => {
                SimpleDoor props = SaveUtils.InstantiatePropsFromSave(data) as SimpleDoor;
            });

            // Instantiate all elevators
            sceneData.elevatorTeleporters?.ToList().ForEach(data => {
                ElevatorTeleporter props = SaveUtils.InstantiatePropsFromSave(data) as ElevatorTeleporter;
                props.SetDestination((PlacesEnum) Enum.Parse(typeof(PlacesEnum), data.destination), RpcTarget.All);
            });

            // Instantiate all packages
            sceneData.packages?.ToList().ForEach(data => {
                Package props = SaveUtils.InstantiatePropsFromSave(data) as Package;
                props.SetPropsInside(data.propsConfigIdInside, RpcTarget.All);
            });

            // Instantiate all buckets
            sceneData.buckets?.ToList().ForEach(data => {
                PaintBucket props = SaveUtils.InstantiatePropsFromSave(data) as PaintBucket;
                props.SetPaintConfigId(data.paintConfigId, RpcTarget.All);

                if (data.color != null) {
                    props.SetColor(data.color, RpcTarget.All);
                }
            });

            // Instantiate all other props
            sceneData.props?.ToList().ForEach(data => SaveUtils.InstantiatePropsFromSave(data));

            this.GenerateNavMesh(true);

            this.IdentifyExteriorWalls();

            // Tell to room that it's generated
            ExitGames.Client.Photon.Hashtable properties = PhotonNetwork.CurrentRoom.CustomProperties;
            properties.Add("isGenerated", true);
            PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        }

        private void GenerateNavMesh(bool locally) {
            if (locally) {
                this.RPC_GenerateNavMesh();
            } else {
                this.photonView.RPC("RPC_GenerateNavMesh", PhotonNetwork.LocalPlayer);
            }
        }

        private void IdentifyExteriorWalls() {
            foreach (Wall wall in FindObjectsOfType<Wall>()) {
                wall.CheckExteriorWall();
            }
        }

        [PunRPC]
        public void RPC_GenerateNavMesh() {
            foreach (NavMeshSurface navMeshSurface in FindObjectsOfType<NavMeshSurface>()) {
                navMeshSurface.BuildNavMesh();
            }
            
            InstantiateLocalPlayer(NetworkManager.Instance.PlayerPrefab, NetworkManager.Instance.Personnage);
            
            LoadingManager.Instance.Hide();
        }

        #endregion

        #region Save

        /**
         * Used to save a room 
         */
        public void SaveRoom() {
            this.photonView.RPC("RPC_SaveRoom", RpcTarget.MasterClient);
        }

        [PunRPC]
        public void RPC_SaveRoom() {
            if (saveCoroutine != null) {
                StopCoroutine(this.saveCoroutine);
            }

            this.saveCoroutine = StartCoroutine(this.SaveRoomDelayed());
        }

        private IEnumerator SaveRoomDelayed() {
            yield return this.saveDelay;

            this.Save(this.GenerateSceneData());
        }

        protected virtual SceneData GenerateSceneData() {
            SceneData sceneData = new SceneData();
            sceneData.doorTeleporters = FindObjectsOfType<DoorTeleporter>().ToList().Select(door => SaveUtils.CreateDoorTeleporterData(door)).ToArray();
            sceneData.elevatorTeleporters =
                FindObjectsOfType<ElevatorTeleporter>().ToList().Select(elevator => SaveUtils.CreateElevatorTeleporterData(elevator)).ToArray();
            sceneData.walls = FindObjectsOfType<Wall>().ToList().Select(wall => SaveUtils.CreateWallData(wall)).ToArray();
            sceneData.simpleDoors = FindObjectsOfType<SimpleDoor>().ToList().Select(door => SaveUtils.CreateDoorData(door)).ToArray();
            sceneData.grounds = FindObjectsOfType<Ground>().ToList().Select(ground => SaveUtils.CreateGroundData(ground)).ToArray();
            sceneData.props = FindObjectsOfType<Props>().ToList().Where(props => { return props.GetType() == typeof(Props) || props.GetType() == typeof(Seat); })
                .Select(props => SaveUtils.CreateDefaultData(props)).ToArray();
            sceneData.packages = FindObjectsOfType<Package>().ToList().Select(package => SaveUtils.CreatePackageData(package)).ToArray();
            sceneData.buckets = FindObjectsOfType<PaintBucket>().ToList().Select(bucket => SaveUtils.CreateBucketData(bucket)).ToArray();

            return sceneData;
        }

        protected virtual void Save(SceneData sceneData) {
            String sceneDataJson = JsonConvert.SerializeObject(sceneData);
            File.WriteAllText(Application.dataPath + "/Resources/PresetSceneDatas/" + SceneManager.GetActiveScene().name + ".json", sceneDataJson);
            Debug.Log("Saved locally");
        }

        #endregion

        #region Player

        public void InstantiateLocalPlayer(GameObject prefab, Personnage personnage) {
            GameObject playerObj = PhotonNetwork.Instantiate("Prefabs/Personnage/" + prefab.name, this.playerSpawnPoint.transform.position, Quaternion.identity);
            LocalPlayer = playerObj.GetComponent<Player>();
        }

        public void MovePlayerTo(Vector3 target) {
            LocalPlayer.SetTarget(target);
        }

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) {
            Debug.Log(newPlayer.NickName + " joined the room");

            if (PhotonNetwork.IsMasterClient) {
                this.StartCoroutine(this.SynchronizeRoomForTarget(newPlayer));
            }
        }

        private IEnumerator SynchronizeRoomForTarget(Photon.Realtime.Player newPlayer) {
            while (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("isGenerated")) {
                yield return null;
            }
            
            foreach (Props props in FindObjectsOfType<Props>()) {
                props.Synchronize(newPlayer);
            }

            this.photonView.RPC("RPC_GenerateNavMesh", newPlayer);
        }

        #endregion
    }
}