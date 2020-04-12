using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class RoomManager : MonoBehaviourPun {
        public static RoomManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
        }

        public void InstantiateLocalPlayer(GameObject prefab, Personnage personnage) {
            PhotonNetwork.Instantiate("Prefabs/" + prefab.name, Vector3.zero, Quaternion.identity);
        }
    }    
}

