using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class RoomManager : MonoBehaviourPun {

        [Header("Only for debug")]
        public static Player LocalPlayer;
        
        public static RoomManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
        }

        public void InstantiateLocalPlayer(GameObject prefab, Personnage personnage) {
            GameObject playerObj = PhotonNetwork.Instantiate("Prefabs/" + prefab.name, Vector3.zero, Quaternion.identity);
            LocalPlayer = playerObj.GetComponent<Player>();
            CameraManager.Instance.SetCameraTarget(LocalPlayer.GetHeadTargetForCamera());
        }

        public void MovePlayerTo(Vector3 target) {
            LocalPlayer.SetTarget(target);
        }
    }    
}

