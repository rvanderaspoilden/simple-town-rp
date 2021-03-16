using System;
using System.Collections;
using System.IO;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Interactables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Sim {
    public class RoomManager : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField]
        protected Transform playerSpawnPoint;

        private readonly WaitForSeconds saveDelay = new WaitForSeconds(1);
        private Coroutine saveCoroutine;

        private bool generated;

        private bool forceWallHidden;

        private bool forcePropsHidden;

        public delegate void VisibilityModeChanged(VisibilityModeEnum mode);

        public static event VisibilityModeChanged OnWallVisibilityModeChanged;

        public static event VisibilityModeChanged OnPropsVisibilityModeChanged;

        public static Character LocalCharacter;

        public static RoomManager Instance;

        protected virtual void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.H) && CameraManager.Instance.GetMode() == CameraModeEnum.FREE) {
                this.ToggleWallVisibility();
            }
        }

        #region Wall Visibility Management

        public void SetWallVisibility(VisibilityModeEnum mode) {
            this.forceWallHidden = mode == VisibilityModeEnum.FORCE_HIDE;

            this.UpdateWallVisibility(mode);
        }

        public void ToggleWallVisibility() {
            this.forceWallHidden = !this.forceWallHidden;

            this.UpdateWallVisibility(this.forceWallHidden ? VisibilityModeEnum.FORCE_HIDE : VisibilityModeEnum.AUTO);
        }

        private void UpdateWallVisibility(VisibilityModeEnum mode) {
            FindObjectsOfType<Wall>().ToList().Where(x => !x.IsExteriorWall()).Select(x => x.GetComponent<PropsRenderer>()).ToList().ForEach(propsRenderer => {
                if (propsRenderer) {
                    propsRenderer.SetVisibilityMode(mode);
                }
            });

            OnWallVisibilityModeChanged?.Invoke(mode);
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
            FindObjectsOfType<Props>().ToList().Where(x => x.GetType() != typeof(Wall)).Select(x => x.GetComponent<PropsRenderer>()).ToList().ForEach(
                propsRenderer => {
                    if (propsRenderer && propsRenderer.IsHideable()) {
                        propsRenderer.SetVisibilityMode(mode);
                    }
                });

            OnPropsVisibilityModeChanged?.Invoke(mode);
        }

        #endregion

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
                props.SetDestination((RoomTypeEnum) Enum.Parse(typeof(RoomTypeEnum), data.destination), RpcTarget.All);
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
                props.SetDestination((RoomTypeEnum) Enum.Parse(typeof(RoomTypeEnum), data.destination), RpcTarget.All);
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
            Hashtable properties = PhotonNetwork.CurrentRoom.CustomProperties;
            properties.Add("isGenerated", true);
            PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        }

        public bool IsGenerated() {
            return this.generated;
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

            this.generated = true;
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
            sceneData.doorTeleporters = FindObjectsOfType<DoorTeleporter>().ToList().Select(SaveUtils.CreateDoorTeleporterData).ToArray();
            sceneData.elevatorTeleporters = FindObjectsOfType<ElevatorTeleporter>().ToList().Select(SaveUtils.CreateElevatorTeleporterData).ToArray();
            sceneData.walls = FindObjectsOfType<Wall>().ToList().Select(SaveUtils.CreateWallData).ToArray();
            sceneData.simpleDoors = FindObjectsOfType<SimpleDoor>().ToList().Select(SaveUtils.CreateDoorData).ToArray();
            sceneData.grounds = FindObjectsOfType<Ground>().ToList().Select(SaveUtils.CreateGroundData).ToArray();
            sceneData.packages = FindObjectsOfType<Package>().ToList().Select(SaveUtils.CreatePackageData).ToArray();
            sceneData.buckets = FindObjectsOfType<PaintBucket>().ToList().Select(SaveUtils.CreateBucketData).ToArray();
            sceneData.props = FindObjectsOfType<Props>().ToList()
                .Where(props => props.GetType() == typeof(Props) || props.GetType() == typeof(Seat))
                .Select(SaveUtils.CreateDefaultData).ToArray();

            return sceneData;
        }

        protected virtual void Save(SceneData sceneData) {
            String sceneDataJson = JsonUtility.ToJson(sceneData);
            File.WriteAllText(Application.dataPath + "/Resources/PresetSceneDatas/" + SceneManager.GetActiveScene().name + ".json", sceneDataJson);
            Debug.Log("Saved locally");
        }

        #endregion

        #region Player

        public virtual void InstantiateLocalCharacter(Character prefab, CharacterData characterData) {
            GameObject character = PhotonNetwork.Instantiate("Prefabs/Characters/" + prefab.name, this.playerSpawnPoint.transform.position, Quaternion.identity);
            LocalCharacter = character.GetComponent<Character>();
            LocalCharacter.CharacterData = characterData;
        }

        public override void OnPlayerEnteredRoom(Player newPlayer) {
            Debug.Log(newPlayer.NickName + " joined the room");

            if (PhotonNetwork.IsMasterClient) {
                this.StartCoroutine(this.SynchronizeRoomForTarget(newPlayer));
            }
        }

        private IEnumerator SynchronizeRoomForTarget(Player newPlayer) {
            while (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("isGenerated")) {
                yield return null;
            }

            foreach (Props props in FindObjectsOfType<Props>()) {
                props.Synchronize(newPlayer);
            }

            this.photonView.RPC("RPC_GenerateNavMesh", newPlayer);
        }

        public override void OnMasterClientSwitched(Player newMasterClient) {
            Debug.Log("Masterclient is now : " + newMasterClient.NickName);
            foreach (Props props in FindObjectsOfType<Props>()) {
                props.RefreshAllActions();
            }
        }

        #endregion
    }
}