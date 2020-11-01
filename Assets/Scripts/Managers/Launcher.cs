using System;
using Photon.Pun;
using Sim.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class Launcher : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private TextMeshProUGUI errorText;

        [SerializeField] private Image statusImg;

        private string username;
        private string password;

        private void Awake() {
            ApiManager.OnAuthenticationSucceeded += this.OnAuthenticationSucceeded;
            ApiManager.OnAuthenticationFailed += this.OnAuthenticationFailed;
            ApiManager.OnServerStatusChanged += this.OnServerStatusChanged;
        }

        private void Start() {
            ApiManager.instance.CheckServerStatus();
        }

        private void OnDestroy() {
            ApiManager.OnAuthenticationSucceeded -= this.OnAuthenticationSucceeded;
            ApiManager.OnAuthenticationFailed -= this.OnAuthenticationFailed;
            ApiManager.OnServerStatusChanged -= this.OnServerStatusChanged;
        }

        public void SetUsername(string username) => this.username = username;

        public void SetPassword(string password) => this.password = password;

        public void Play() {
            if (username != String.Empty && password != String.Empty) {
                PhotonNetwork.NickName = username;

                this.ResetErrorText();

                ApiManager.instance.Authenticate(username, password);
            }
        }

        private void ResetErrorText() => this.errorText.text = String.Empty;

        #region Callbacks

        private void OnAuthenticationSucceeded(Personnage personnage) {
            NetworkManager.Instance.Play(personnage);
        }

        private void OnAuthenticationFailed(String msg) => this.errorText.text = msg;

        private void OnServerStatusChanged(bool isActive) => this.statusImg.color = isActive ? Color.green : Color.red;

        #endregion
    }
}