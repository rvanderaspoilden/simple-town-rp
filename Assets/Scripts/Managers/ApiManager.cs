using System;
using System.Collections;
using System.Text;
using Sim.Entities;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim {
    public class ApiManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private String uri = "http://localhost:3000";

        [SerializeField]
        private bool local;

        [Header("Only for debug")]
        [SerializeField]
        private String accessToken;

        [SerializeField]
        private User user;

        private Coroutine authenticationCoroutine;

        public delegate void SucceededResponse(CharacterData personnage);

        public delegate void FailedResponse(String msg);

        public static event SucceededResponse OnAuthenticationSucceeded;

        public static event FailedResponse OnAuthenticationFailed;

        public delegate void ServerStatus(bool isActive);

        public static event ServerStatus OnServerStatusChanged;

        public static event SucceededResponse OnCharacterCreated;
        public static event FailedResponse OnCharacterCreationFailed;


        public static ApiManager instance;

        private void Awake() {
            if (instance == null) {
                instance = this;
            } else {
                Destroy(this.gameObject);
            }

            if (this.local) {
                this.uri = "http://localhost:3000";
            }
        }

        public void Authenticate(String username, String password) {
            this.authenticationCoroutine ??= StartCoroutine(this.AuthenticationCoroutine(username, password));
        }

        public void SaveAppartment(String id, String owner, SceneData sceneData) {
            StartCoroutine(this.SaveAppartmentCoroutine(id, owner, sceneData));
        }

        public UnityWebRequest RetrieveHomeById(int homeId) {
            UnityWebRequest homeRequest = UnityWebRequest.Get(this.uri + "/homes/" + homeId);
            homeRequest.SendWebRequest();
            return homeRequest;
        }

        public void CreateCharacter(CharacterCreationRequest data) {
            StartCoroutine(this.CreateCharacterCoroutine(data));
        }

        private IEnumerator CreateCharacterCoroutine(CharacterCreationRequest data) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(data));

            UnityWebRequest request = new UnityWebRequest(this.uri + "/characters", "POST") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            if (request.responseCode == 201) {
                CharacterResponse characterResponse = JsonUtility.FromJson<CharacterResponse>(request.downloadHandler?.text);
                OnCharacterCreated?.Invoke(characterResponse.Characters[0]);
            } else {
                OnCharacterCreationFailed?.Invoke(ExtractErrorMessage(request));
            }
        }

        public void CheckServerStatus() {
            StartCoroutine(this.CheckServerStatusCoroutine());
        }

        private IEnumerator SaveAppartmentCoroutine(String id, String owner, SceneData sceneData) {
            // TODO save home
            /*Home home = new HomeResponse(id, owner, sceneData);
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(appartment));

            UnityWebRequest request = new UnityWebRequest(this.uri + "/appartment/" + id, "POST");
            request.uploadHandler = new UploadHandlerRaw(encodedPayload);
            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");

            yield return request.SendWebRequest();

            if (request.responseCode == 201) {
                Debug.Log("Saved successfully");
            } else {
                Debug.Log(request.error);
            }*/
            return null;
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

            if (authRequest.responseCode == 201) {
                // If credentials are valid
                AuthenticationResponse response = JsonUtility.FromJson<AuthenticationResponse>(authRequest.downloadHandler.text);
                this.accessToken = response.GetAccessToken();

                // retrieve profile
                UnityWebRequest profileRequest = UnityWebRequest.Get(this.uri + "/auth/profile");
                profileRequest.SetRequestHeader("Authorization", "Bearer " + this.accessToken);

                yield return profileRequest.SendWebRequest();

                if (profileRequest.responseCode == 200) {
                    ProfileResponse profileResponse = JsonUtility.FromJson<ProfileResponse>(profileRequest.downloadHandler.text);
                    this.user = profileResponse.User;

                    // retrieve user's characters
                    UnityWebRequest characterRequest = UnityWebRequest.Get(this.uri + "/characters/by-user-id/" + this.user.Id);

                    yield return characterRequest.SendWebRequest();

                    if (characterRequest.responseCode == 200) {
                        CharacterResponse characterResponse = JsonUtility.FromJson<CharacterResponse>(characterRequest.downloadHandler.text);

                        if (characterResponse.Characters != null && characterResponse.Characters.Length > 0) {
                            OnAuthenticationSucceeded?.Invoke(characterResponse.Characters[0]);
                        } else {
                            OnAuthenticationSucceeded?.Invoke(null);
                        }
                    } else {
                        OnAuthenticationFailed?.Invoke(ExtractErrorMessage(characterRequest));
                    }
                } else {
                    OnAuthenticationFailed?.Invoke(ExtractErrorMessage(profileRequest));
                }
            } else {
                OnAuthenticationFailed?.Invoke(ExtractErrorMessage(authRequest));
            }

            this.authenticationCoroutine = null;
        }

        private string ExtractErrorMessage(UnityWebRequest request) {
            HttpException exception = JsonUtility.FromJson<HttpException>(request.downloadHandler.text);
            return exception != null ? exception?.Message : request.error;
        }
    }
}