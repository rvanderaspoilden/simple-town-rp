using System;
using System.Collections;
using System.Configuration;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim {
    public class ApiManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private String uri = "http://localhost:3000";

        [Header("Only for debug")]
        [SerializeField] private String accessToken;

        private Coroutine authenticationCoroutine;

        public delegate void AuthenticationRequest(UnityWebRequest webRequest);

        public static event AuthenticationRequest OnAuthenticationSucceeded;

        public static event AuthenticationRequest OnAuthenticationFailed;

        public delegate void ServerStatus(bool isActive);

        public static event ServerStatus OnServerStatusChanged;

        public static ApiManager instance;

        private void Awake() {
            if (instance == null) {
                instance = this;
            } else {
                Destroy(this);
            }
        }

        public void Authenticate(String username, String password) {
            if (this.authenticationCoroutine == null) {
                this.authenticationCoroutine = StartCoroutine(this.AuthenticationCoroutine(username, password));
            }
        }

        public void CheckServerStatus() {
            StartCoroutine(this.CheckServerStatusCoroutine());
        }

        private IEnumerator CheckServerStatusCoroutine() {
            UnityWebRequest www = UnityWebRequest.Get(this.uri + "/hc");

            yield return www.SendWebRequest();
            
            OnServerStatusChanged?.Invoke(www.responseCode == 200);
        }

        private IEnumerator AuthenticationCoroutine(String username, String password) {
            WWWForm form = new WWWForm();
            form.AddField("username", username);
            form.AddField("password", password);

            UnityWebRequest www = UnityWebRequest.Post(this.uri + "/auth/login", form);

            yield return www.SendWebRequest();

            if (www.responseCode == 201) {
                AuthenticationResponse response = JsonUtility.FromJson<AuthenticationResponse>(www.downloadHandler.text);
                this.accessToken = response.GetAccessToken();
                OnAuthenticationSucceeded?.Invoke(www);
            } else if (www.responseCode == 401 || www.isNetworkError) {
                OnAuthenticationFailed?.Invoke(www);
            }

            this.authenticationCoroutine = null;
        }
    }
}