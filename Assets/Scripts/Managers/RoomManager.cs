using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Photon.Pun;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Interactables;
using Sim.Scriptables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Sim {
    public class RoomManager : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField] private Transform playerSpawnPoint;

        [Header("Only for debug")]
        public static Player LocalPlayer;

        public static RoomManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
        }

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
        }

        public void SaveRoom() {
            SceneData sceneData = new SceneData();
            sceneData.doorTeleporters = FindObjectsOfType<DoorTeleporter>().ToList().Select(door => SaveUtils.CreateDoorTeleporterData(door)).ToArray();
            sceneData.elevatorTeleporters = FindObjectsOfType<ElevatorTeleporter>().ToList().Select(elevator => SaveUtils.CreateElevatorTeleporterData(elevator)).ToArray();
            sceneData.walls = FindObjectsOfType<Wall>().ToList().Select(wall => SaveUtils.CreateWallData(wall)).ToArray();
            sceneData.simpleDoors = FindObjectsOfType<SimpleDoor>().ToList().Select(door => SaveUtils.CreateDoorData(door)).ToArray();
            sceneData.grounds = FindObjectsOfType<Ground>().ToList().Select(ground => SaveUtils.CreateGroundData(ground)).ToArray();
            sceneData.props = FindObjectsOfType<Props>().ToList().Where(props => {
                return props.GetType() == typeof(Props) || props.GetType() == typeof(Seat);
            }).Select(props => SaveUtils.CreateDefaultData(props)).ToArray();
            sceneData.packages = FindObjectsOfType<Package>().ToList().Select(package => SaveUtils.CreatePackageData(package)).ToArray();
            sceneData.buckets = FindObjectsOfType<PaintBucket>().ToList().Select(bucket => SaveUtils.CreateBucketData(bucket)).ToArray();

            String sceneDataJson = JsonConvert.SerializeObject(sceneData);
            System.IO.File.WriteAllText(Application.dataPath + "/Resources/PresetSceneDatas/" + SceneManager.GetActiveScene().name + ".json", sceneDataJson);
            Debug.Log("SAVED");
        }

        public void InstantiateLocalPlayer(GameObject prefab, Personnage personnage) {
            GameObject playerObj = PhotonNetwork.Instantiate("Prefabs/Personnage/" + prefab.name, this.playerSpawnPoint.transform.position, Quaternion.identity);
            LocalPlayer = playerObj.GetComponent<Player>();
            CameraManager.Instance.SetCameraTarget(LocalPlayer.GetHeadTargetForCamera());
        }

        public void MovePlayerTo(Vector3 target) {
            LocalPlayer.SetTarget(target);
        }

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) {
            Debug.Log("New player joined the room");
            this.photonView.RPC("RPC_UpdateRoom", newPlayer);

            foreach (Props props in FindObjectsOfType<Props>()) {
                props.Synchronize(newPlayer);
            }
        }
        
        [PunRPC]
        public void RPC_UpdateRoom() {
            Debug.Log("I have to update my room");
        }
    }
}