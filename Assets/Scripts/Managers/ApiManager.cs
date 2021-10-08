using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sim.Entities;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

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

        public delegate void SucceededResponse();
        
        public delegate void HomeCreationSuceededResponse(Home home);

        public delegate void CharacterDataResponse(CharacterData characterData);

        public delegate void HomesResponse(List<Home> homes);

        public delegate void FailedResponse(String msg);

        public static event SucceededResponse OnAuthenticationSucceeded;

        public static event FailedResponse OnAuthenticationFailed;

        public delegate void ServerStatus(bool isActive);

        public static event ServerStatus OnServerStatusChanged;

        public static event CharacterDataResponse OnCharacterCreated;

        public static event CharacterDataResponse OnCharacterRetrieved;
        public static event FailedResponse OnCharacterCreationFailed;

        public static event HomesResponse OnHomesRetrieved;

        public static event HomeCreationSuceededResponse OnApartmentAssigned;

        public static event FailedResponse OnApartmentAssignmentFailed;
        

        public static ApiManager Instance;

        private void Awake() {
            if (Instance == null) {
                Instance = this;
            } else {
                Destroy(this.gameObject);
            }

            if (this.local) {
                this.uri = "http://localhost:3000";
            }

            DontDestroyOnLoad(this.gameObject);
        }

        public void Authenticate(String username, String password) {
            this.authenticationCoroutine ??= StartCoroutine(this.AuthenticationCoroutine(username, password));
        }

        public UnityWebRequest SaveHomeRequest(Home home, SceneData sceneData) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(sceneData));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/homes/{home.Id}", "PUT") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");

            return request;
        }

        public UnityWebRequest RetrieveHomeRequest(Address address) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(address));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/homes/by-address", "POST") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            
            return request;
        }

        public void AssignApartment(AssignApartmentRequest request) {
            StartCoroutine(this.AssignApartmentCoroutine(request));
        }

        private IEnumerator AssignApartmentCoroutine(AssignApartmentRequest data) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(data));

            UnityWebRequest request = new UnityWebRequest(this.uri + "/homes/assign-apartment", "POST") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            if (request.responseCode == 201) {
                Home home = JsonUtility.FromJson<Home>(request.downloadHandler?.text);
                OnApartmentAssigned?.Invoke(home);
            } else {
                Debug.Log(ExtractErrorMessage(request));
                OnApartmentAssignmentFailed?.Invoke(ExtractErrorMessage(request));
            }
        }
        
        public UnityWebRequest CreateUserRequest(CreateUserRequest data) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(data));

            UnityWebRequest request = new UnityWebRequest(this.uri + "/users/signup", "POST") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            return request;
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

        public void RetrieveCharacters() {
            StartCoroutine(this.RetrieveCharactersCoroutine());
        }

        public UnityWebRequest RetrieveCharacterByUserIdRequest(string userId) {
            return UnityWebRequest.Get($"{this.uri}/characters/by-user-id/{userId}");
        }
        
        public UnityWebRequest RetrieveCharacterByIdRequest(string id) {
            return UnityWebRequest.Get($"{this.uri}/characters/{id}");
        }

        private IEnumerator RetrieveCharactersCoroutine(Action<CharacterData> action = null) {
            UnityWebRequest characterRequest = UnityWebRequest.Get(this.uri + "/characters/by-user-id/" + this.user.Id);

            yield return characterRequest.SendWebRequest();

            if (characterRequest.responseCode == 200) {
                CharacterResponse characterResponse = JsonUtility.FromJson<CharacterResponse>(characterRequest.downloadHandler.text);

                OnCharacterRetrieved?.Invoke(characterResponse.Characters[0]);

                action?.Invoke(characterResponse.Characters[0]);
            } else {
                OnCharacterRetrieved?.Invoke(null);
            }
        }

        public UnityWebRequest DeleteDeliveryRequest(Delivery delivery) {
            UnityWebRequest request = UnityWebRequest.Delete($"{this.uri}/deliveries/{delivery._id}");
            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            return request;
        }
        
        public UnityWebRequest CreateDeliveryRequest(CreateDeliveryRequest body) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(body));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/deliveries", "POST") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            
            return request;
        }

        public UnityWebRequest RetrieveDeliveriesRequest(string characterId) {
            UnityWebRequest request = UnityWebRequest.Get($"{this.uri}/characters/{characterId}/deliveries");
            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);

            return request;
        }
        
        public UnityWebRequest RetrieveCityRequest(string cityName) {
            return UnityWebRequest.Get($"{this.uri}/city/by-name/{cityName}");
        }
        
        public UnityWebRequest UpdateCityTimestampRequest(CityUpdateTimestampRequest body) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(body));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/city/{body.id}", "PUT") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");

            return request;
        }

        public void RetrieveHomesByCharacter(CharacterData characterData) {
            StartCoroutine(this.RetrieveHomesByCharacterCoroutine(characterData));
        }

        public UnityWebRequest RetrieveHomesByCharacterRequest(CharacterData characterData) {
            return UnityWebRequest.Get($"{this.uri}/characters/{characterData.Id}/homes");
        }

        private IEnumerator RetrieveHomesByCharacterCoroutine(CharacterData characterData) {
            UnityWebRequest request = UnityWebRequest.Get($"{this.uri}/characters/{characterData.Id}/homes");

            yield return request.SendWebRequest();

            if (request.responseCode == 200) {
                HomeResponse homeResponse = JsonUtility.FromJson<HomeResponse>(request.downloadHandler.text);

                OnHomesRetrieved?.Invoke(homeResponse.Homes.ToList());
            } else {
                OnHomesRetrieved?.Invoke(null);
            }
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
                    OnAuthenticationSucceeded?.Invoke();
                } else {
                    OnAuthenticationFailed?.Invoke(ExtractErrorMessage(profileRequest));
                }
            } else {
                OnAuthenticationFailed?.Invoke(ExtractErrorMessage(authRequest));
            }

            this.authenticationCoroutine = null;
        }

        public static string ExtractErrorMessage(UnityWebRequest request) {
            HttpException exception = JsonUtility.FromJson<HttpException>(request.downloadHandler.text);
            return exception != null ? exception?.Message : request.error;
        }
    }
}