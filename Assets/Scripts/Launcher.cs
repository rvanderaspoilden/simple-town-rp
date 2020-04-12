using System;
using Photon.Pun;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class Launcher : MonoBehaviour {
        [Header("Only for debug")]
        [SerializeField] private string username;

        public void SetUsername(string username) {
            this.username = username;
        }

        public void Play() {
            if (username != String.Empty) {
                PhotonNetwork.NickName = username;

                Personnage personnage = new Personnage("Stanislas", "Duquebec");

                NetworkManager.Instance.Play(personnage);
            }
        }
    }
}