using System;
using System.Collections;
using Sim.Entities;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim {
    public class ApiManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private String uri = "http://localhost:3000";

        [Header("Only for debug")]
        [SerializeField] private String accessToken;

        [SerializeField] private User user;

        private Coroutine authenticationCoroutine;

        public delegate void AuthenticationSucceededResponse(Personnage personnage);

        public delegate void AuthenticationFailedResponse(String msg);

        public static event AuthenticationSucceededResponse OnAuthenticationSucceeded;

        public static event AuthenticationFailedResponse OnAuthenticationFailed;

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

            UnityWebRequest authRequest = UnityWebRequest.Post(this.uri + "/auth/login", form);

            yield return authRequest.SendWebRequest();

            if (authRequest.responseCode == 201) { // If credentials are valid
                AuthenticationResponse response = JsonUtility.FromJson<AuthenticationResponse>(authRequest.downloadHandler.text);
                this.accessToken = response.GetAccessToken();

                // retrieve profile
                UnityWebRequest profileRequest = UnityWebRequest.Get(this.uri + "/auth/profile");
                profileRequest.SetRequestHeader("Authorization", "Bearer " + this.accessToken);

                yield return profileRequest.SendWebRequest();

                if (profileRequest.responseCode == 200) {
                    ProfileResponse profileResponse = JsonUtility.FromJson<ProfileResponse>(profileRequest.downloadHandler.text);
                    this.user = profileResponse.User;

                    // retrive user's personnages
                    UnityWebRequest personnageRequest = UnityWebRequest.Get(this.uri + "/personnage/" + this.user.Id);

                    yield return personnageRequest.SendWebRequest();

                    if (personnageRequest.responseCode == 200) {
                        PersonnageResponse personnageResponse = JsonUtility.FromJson<PersonnageResponse>(personnageRequest.downloadHandler.text);

                        if (personnageResponse.Personnages != null && personnageResponse.Personnages.Length > 0) {
                            OnAuthenticationSucceeded?.Invoke(personnageResponse.Personnages[0]);
                        } else {
                            OnAuthenticationFailed?.Invoke("No personnage found for this account");
                        }
                    }
                } else {
                    OnAuthenticationFailed?.Invoke("An error occured");
                }
            } else if (authRequest.responseCode == 401 || authRequest.isNetworkError) {
                OnAuthenticationFailed?.Invoke("Username or password invalid");
            }

            this.authenticationCoroutine = null;
        }
    }
}