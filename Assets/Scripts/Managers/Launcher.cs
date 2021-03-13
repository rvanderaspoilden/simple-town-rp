using System;
using System.Collections;
using Photon.Pun;
using Sim.Entities;
using TMPro;
using UnityEditor.Timeline.Actions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sim {
    public class Launcher : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private TextMeshProUGUI errorText;

        [SerializeField]
        private TMP_InputField pseudoInputField;

        [SerializeField]
        private TMP_InputField passwordInputField;

        [SerializeField]
        private bool debug;

        [SerializeField]
        private Image statusImg;

        private string username;
        private string password;

        private void Awake() {
            ApiManager.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
            ApiManager.OnAuthenticationFailed += this.OnAuthenticationFailed;
            ApiManager.OnServerStatusChanged += this.OnServerStatusChanged;
        }

        private void Start() {
            ApiManager.instance.CheckServerStatus();

            this.pseudoInputField.Select();

            if (debug) {
                StartCoroutine(Debug());
            }
        }

        private IEnumerator Debug() {
            yield return new WaitForSeconds(2f);
            this.username = "spectus";
            this.password = "test";
            this.Authenticate();
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Tab)) {
                Selectable next = EventSystem.current.currentSelectedGameObject
                    .GetComponent<Selectable>()
                    .FindSelectableOnDown();

                if (next) next.Select();
            }

            if (Input.GetKeyDown(KeyCode.Return)) {
                this.Authenticate();
            }
        }

        private void OnDestroy() {
            ApiManager.OnAuthenticationSucceeded -= this.OnAuthenticationSucceeded;
            ApiManager.OnAuthenticationFailed -= this.OnAuthenticationFailed;
            ApiManager.OnServerStatusChanged -= this.OnServerStatusChanged;
        }

        public void SetUsername(string username) => this.username = username;

        public void SetPassword(string password) => this.password = password;

        public void Authenticate() {
            if (username == string.Empty || password == string.Empty) return;
            
            this.ResetErrorText();

            ApiManager.instance.Authenticate(username, password);
        }

        private void ResetErrorText() => this.errorText.text = String.Empty;

        #region Callbacks

        private void OnAuthenticationSucceeded() {
            SceneManager.LoadScene("Main Menu");
        }

        private void OnAuthenticationFailed(String msg) => this.errorText.text = msg;

        private void OnServerStatusChanged(bool isActive) => this.statusImg.color = isActive ? Color.green : Color.red;

        #endregion
    }
}