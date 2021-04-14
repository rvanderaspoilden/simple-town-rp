using System;
using System.Collections;
using System.IO;
using System.Linq;
using Sim.Building;
using Sim.Enums;
using Sim.Utils;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Sim {
    public class RoomManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        protected Transform playerSpawnPoint;

        private readonly WaitForSeconds saveDelay = new WaitForSeconds(1);
        private Coroutine saveCoroutine;

        private bool generated;

        private bool forceWallHidden;

        private bool forcePropsHidden;

        private Type[] defaultPropsTypes = new[] {typeof(Props), typeof(Seat), typeof(DeliveryBox)};

        public delegate void VisibilityModeChanged(VisibilityModeEnum mode);

        public static event VisibilityModeChanged OnWallVisibilityModeChanged;

        public static event VisibilityModeChanged OnPropsVisibilityModeChanged;

        public static PlayerController LocalPlayer;

        public static RoomManager Instance;

        protected virtual void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
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
            /*FindObjectsOfType<Wall>().ToList().Where(x => !x.IsExteriorWall()).Select(x => x.GetComponent<PropsRenderer>()).ToList().ForEach(propsRenderer => {
                if (propsRenderer) {
                    propsRenderer.SetVisibilityMode(mode);
                }
            });

            OnWallVisibilityModeChanged?.Invoke(mode);*/
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

        /*public void InstantiateLevel(SceneData sceneData, RoomNavigationData currentRoom, RoomNavigationData oldRoom) {
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

                if (currentRoom.RoomType == RoomTypeEnum.BUILDING_HALL && oldRoom.RoomType == RoomTypeEnum.HOME) {
                    props.SetDoorNumber(CommonUtils.GetDoorNumberFromFloorNumber(data.number, oldRoom.Address.DoorNumber), RpcTarget.All);
                } else if (currentRoom.RoomType == RoomTypeEnum.HOME) {
                    props.SetDoorNumber(currentRoom.Address.DoorNumber, RpcTarget.All);
                }
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
        }*/

        public bool IsGenerated() {
            return this.generated;
        }

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
            //this.photonView.RPC("RPC_SaveRoom", RpcTarget.MasterClient);
        }

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
            SceneData sceneData = new SceneData {
                //walls = FindObjectsOfType<Wall>().ToList().Select(SaveUtils.CreateWallData).ToArray(),
                //simpleDoors = FindObjectsOfType<SimpleDoor>().ToList().Select(SaveUtils.CreateDoorData).ToArray(),
                //grounds = FindObjectsOfType<Ground>().ToList().Select(SaveUtils.CreateGroundData).ToArray(),
                buckets = FindObjectsOfType<PaintBucket>().ToList().Select(SaveUtils.CreateBucketData).ToArray(),
                props = FindObjectsOfType<Props>().ToList()
                    .Where(props => defaultPropsTypes.Contains(props.GetType()))
                    .Select(SaveUtils.CreateDefaultData).ToArray()
            };

            return sceneData;
        }

        protected virtual void Save(SceneData sceneData) {
            String sceneDataJson = JsonUtility.ToJson(sceneData);
            File.WriteAllText(Application.dataPath + "/Resources/PresetSceneDatas/" + SceneManager.GetActiveScene().name + ".json", sceneDataJson);
            Debug.Log("Saved locally");
        }

        #endregion

        #region Player

        /*public virtual void InstantiateLocalCharacter(Character prefab, CharacterData characterData, RoomNavigationData currentRoom, RoomNavigationData oldRoom) {
            Transform spawn = this.playerSpawnPoint;

            if (currentRoom.RoomType == RoomTypeEnum.BUILDING_HALL && oldRoom.RoomType == RoomTypeEnum.HOME) {
                Teleporter teleporter = FindObjectsOfType<DoorTeleporter>().First(doorTeleporter => doorTeleporter.GetDoorNumber() == oldRoom.Address.DoorNumber);

                if (teleporter) {
                    spawn = teleporter.Spawn;
                }
            } else if (currentRoom.RoomType == RoomTypeEnum.HOME) {
                Teleporter teleporter = FindObjectOfType<DoorTeleporter>();

                if (teleporter) {
                    spawn = teleporter.Spawn;
                }
            }

            GameObject character = PhotonNetwork.Instantiate("Prefabs/Characters/" + prefab.name, spawn.position, spawn.rotation);
            LocalCharacter = character.GetComponent<Character>();
            LocalCharacter.CharacterData = characterData;
        }*/
        

        #endregion
    }
}