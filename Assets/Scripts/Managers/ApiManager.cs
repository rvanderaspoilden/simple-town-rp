using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Sim.Entities;
using Sim.Entities.Persistence;
using Sim.Deployment;
#if STRESS_TEST_BOTS
using Sim.StressTest;
#endif
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

        // Cached UUID of the system "transit" place (where bought-but-not-yet-built
        // props sit while they're in delivery). Populated by EnsureTransitPlace() at
        // server boot and stable for the lifetime of the process.
        private string _transitPlaceId;
        public string TransitPlaceId => _transitPlaceId;

        // Cached UUID of the singleton "city" place — the persistence anchor for
        // world items dropped on the ground in the City scene (the city has no
        // apartment place). Populated by EnsureCityPlace() at server boot.
        private string _cityPlaceId;
        public string CityPlaceId => _cityPlaceId;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
            NullValueHandling   = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include,
        };

        private void Awake() {
            if (Instance == null) {
                Instance = this;
            } else {
                Destroy(this.gameObject);
            }

            // Step 1 — runtime default from the EnvironmentSelector (PlayerPrefs-backed,
            // driven by the Launcher dropdown). Overrides below win over this.
            this.uri = EnvironmentSelector.Current.ApiUri;

#if STRESS_TEST_BOTS
            // Bot mode wins over the `local` toggle: a stress-test build needs to
            // point at whichever server URI the launcher script passed, and the
            // serialized `local=true` we ship in dev scenes would otherwise pin
            // every bot to localhost regardless of --bot-server.
            if (CommandLineArgs.BotMode && !string.IsNullOrEmpty(CommandLineArgs.BotServer)) {
                this.uri = CommandLineArgs.BotServer;
            } else if (this.local) {
                this.uri = "http://localhost:3000";
            }
#else
            if (this.local) {
                this.uri = "http://localhost:3000";
            }
#endif

            // Deployment override: on the VPS, the Mirror server runs with
            // API_URL=http://localhost:3000 exported by run-server.sh. Same
            // mechanism lets a remote tester point a local Editor at a
            // distant backend without rebuilding. Wins over everything else —
            // env vars are the most explicit signal an operator can give.
            string envUri = Environment.GetEnvironmentVariable("API_URL");
            if (!string.IsNullOrEmpty(envUri)) {
                this.uri = envUri;
            }

            DontDestroyOnLoad(this.gameObject);
        }

        public void Authenticate(String username, String password) {
            this.authenticationCoroutine ??= StartCoroutine(this.AuthenticationCoroutine(username, password));
        }

        /// <summary>
        /// Runtime override of the API base URI. Used by the Launcher when the
        /// player switches environment via the dropdown — we don't want to wait
        /// for a domain reload to pick up the new value. No-op on empty input
        /// so a misconfigured EnvironmentRegistry entry can't accidentally wipe
        /// the URI mid-session.
        /// </summary>
        public void SetUri(string newUri) {
            if (string.IsNullOrEmpty(newUri)) return;
            this.uri = newUri;
        }

#if STRESS_TEST_BOTS
        /// <summary>
        /// Stress-test bot path. The /auth/register-bot endpoint already returns
        /// a JWT + the user identity in a single round-trip, so we inject the
        /// state directly instead of replaying the AuthenticationCoroutine.
        /// Only invoked by BotRunner in headless bot builds.
        /// </summary>
        public void SetBotAuth(string token, User identity) {
            this.accessToken = token;
            this.user = identity;
        }

        public string Uri => this.uri;
#endif

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

        public void CreateBugReport(string message, string characterId, Action<bool> onComplete) {
            StartCoroutine(this.CreateBugReportCoroutine(message, characterId, onComplete));
        }

        private IEnumerator CreateBugReportCoroutine(string message, string characterId, Action<bool> onComplete) {
            var payload = new CreateBugReportRequest { message = message, characterId = characterId ?? string.Empty };
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(payload));

            UnityWebRequest request = new UnityWebRequest(this.uri + "/bug-reports", "POST") {
                uploadHandler   = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            bool ok = request.responseCode == 201 || request.responseCode == 200;
            if (!ok) Debug.LogWarning($"[ApiManager] CreateBugReport failed: {ExtractErrorMessage(request)}");
            onComplete?.Invoke(ok);
        }

        /// <summary>
        /// Fire-and-forget persistence of a chat message for moderation tracking.
        /// The instant in-world display is handled separately via Mirror; this call
        /// only stores the message and never blocks or gates the chat.
        /// </summary>
        public void CreateChatMessage(string message, string characterId, string characterName) {
            StartCoroutine(this.CreateChatMessageCoroutine(message, characterId, characterName));
        }

        private IEnumerator CreateChatMessageCoroutine(string message, string characterId, string characterName) {
            var payload = new CreateChatMessageRequest {
                message       = message,
                characterId   = characterId ?? string.Empty,
                characterName = characterName ?? string.Empty,
            };
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(payload));

            UnityWebRequest request = new UnityWebRequest(this.uri + "/chat-messages", "POST") {
                uploadHandler   = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            bool ok = request.responseCode == 201 || request.responseCode == 200;
            if (!ok) Debug.LogWarning($"[ApiManager] CreateChatMessage failed: {ExtractErrorMessage(request)}");
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
            // UnityWebRequest.Delete() ships without a downloadHandler, but the
            // backend echoes the deleted Delivery row (we need its prop_id for
            // the buy→build UUID bridge). Attach a buffer so callers can read
            // `downloadHandler.text` without NRE-ing.
            UnityWebRequest request = UnityWebRequest.Delete($"{this.uri}/deliveries/{delivery._id}");
            request.downloadHandler = new DownloadHandlerBuffer();
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

        public UnityWebRequest UpdateCharacterHealthRequest(string characterId, Health health) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(health));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/characters/{characterId}/update-health", "PUT") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Content-type", "application/json");

            return request;
        }

        public UnityWebRequest UpdateCharacterOnlineStateRequest(string characterId, bool online) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(new CharacterUpdateOnlineStateRequest { online = online }));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/characters/{characterId}/online-state", "PUT") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer(),
            };

            request.SetRequestHeader("Content-type", "application/json");

            return request;
        }

        /// <summary>POST /characters/reset-online-state — clears every character.online flag.
        /// Called by the Unity server at boot to recover from stale flags left over
        /// by a previous crash; the login flow refuses JWT issuance while any
        /// character is online so missing this step locks users out.</summary>
        public UnityWebRequest ResetAllOnlineStateRequest() {
            UnityWebRequest request = new UnityWebRequest($"{this.uri}/characters/reset-online-state", "POST") {
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-type", "application/json");
            return request;
        }

        /// <summary>POST /characters/:id/ledger — single chokepoint for every money
        /// movement. Posts a signed amount + reason; the backend updates the balance
        /// AND records a ledger entry atomically, returning the new balance.</summary>
        public UnityWebRequest PostLedgerEntryRequest(string characterId, PostLedgerBody body) =>
            BuildJsonRequest($"{this.uri}/characters/{characterId}/ledger", "POST", body);

        /// <summary>GET /characters/:id/ledger — full money history, newest first.</summary>
        public UnityWebRequest RetrieveLedgerRequest(string characterId) =>
            BuildJsonRequest($"{this.uri}/characters/{characterId}/ledger", "GET", null);

        // ── Constellation ───────────────────────────────────────────────────

        public static event Action<ConstellationStateData> OnConstellationRetrieved;
        public static event Action<ConstellationStateData> OnConstellationUpdated;

        /// <summary>GET /characters/:id/constellation — full state (wallets +
        /// unlocked node set + last discovery). The row is auto-created at
        /// character insertion via a Postgres trigger.</summary>
        public UnityWebRequest RetrieveConstellationRequest(string characterId) =>
            BuildJsonRequest($"{this.uri}/characters/{characterId}/constellation", "GET", null);

        /// <summary>POST /characters/:id/constellation/unlock — debit the wallet
        /// for the given costs and append the node id to the unlocked set. The
        /// server validates funds but not prerequisites (graph stays
        /// client-authored). Raises HTTP 500 with `INSUFFICIENT_FUNDS:&lt;key&gt;`
        /// if any wallet is short.</summary>
        public UnityWebRequest UnlockConstellationNodeRequest(string characterId, UnlockNodeBody body) =>
            BuildJsonRequest($"{this.uri}/characters/{characterId}/constellation/unlock", "POST", body);

        /// <summary>POST /characters/:id/constellation/grant — additive credit
        /// on branch + profession wallets. Used by the mission reward pipeline
        /// (Unity server-side) and by the in-game debug keys.</summary>
        public UnityWebRequest GrantConstellationPointsRequest(string characterId, GrantPointsBody body) =>
            BuildJsonRequest($"{this.uri}/characters/{characterId}/constellation/grant", "POST", body);

        public void RetrieveConstellation(string characterId) {
            StartCoroutine(SendConstellationCoroutine(
                RetrieveConstellationRequest(characterId),
                state => OnConstellationRetrieved?.Invoke(state)));
        }

        public void UnlockConstellationNode(string characterId, UnlockNodeBody body, Action<ConstellationStateData> onSuccess = null, Action<string> onError = null) {
            StartCoroutine(SendConstellationCoroutine(
                UnlockConstellationNodeRequest(characterId, body),
                state => { OnConstellationUpdated?.Invoke(state); onSuccess?.Invoke(state); },
                onError));
        }

        public void GrantConstellationPoints(string characterId, GrantPointsBody body, Action<ConstellationStateData> onSuccess = null) {
            StartCoroutine(SendConstellationCoroutine(
                GrantConstellationPointsRequest(characterId, body),
                state => { OnConstellationUpdated?.Invoke(state); onSuccess?.Invoke(state); }));
        }

        private IEnumerator SendConstellationCoroutine(UnityWebRequest req, Action<ConstellationStateData> onSuccess, Action<string> onError = null) {
            yield return req.SendWebRequest();
            if (req.responseCode >= 200 && req.responseCode < 300) {
                var resp = JsonUtility.FromJson<ConstellationStateResponse>(req.downloadHandler.text);
                if (resp != null && resp.States != null && resp.States.Length > 0) {
                    onSuccess?.Invoke(resp.States[0]);
                } else {
                    Debug.LogWarning("[Constellation] Empty response: " + req.downloadHandler.text);
                }
            } else {
                Debug.LogError($"[Constellation] HTTP {req.responseCode} on {req.url} — {req.downloadHandler?.text}");
                onError?.Invoke(req.downloadHandler?.text);
            }
            req.Dispose();
        }

        // ── Rent ────────────────────────────────────────────────────────────

        /// <summary>POST /homes/rent/tick — server-driven rent collection pass at
        /// the given in-game time. Charges due homes (catch-up), reverses to the
        /// owner, and evicts non-payers. Returns the evictions for the caller to
        /// act on (teleport + notify).</summary>
        public UnityWebRequest RentTickRequest(long nowInGameSeconds) =>
            BuildJsonRequest($"{this.uri}/homes/rent/tick", "POST",
                new RentTickBody { nowInGameSeconds = nowInGameSeconds });

        // ── Relationships ─────────────────────────────────────────────────────

        public static event Action<List<RelationshipData>> OnRelationshipsRetrieved;

        /// <summary>GET /characters/:id/relationships — all relationships of a character.</summary>
        public UnityWebRequest RetrieveRelationshipsRequest(string characterId) =>
            BuildJsonRequest($"{this.uri}/characters/{characterId}/relationships", "GET", null);

        public void RetrieveRelationships(string characterId) {
            StartCoroutine(RetrieveRelationshipsCoroutine(characterId));
        }

        private IEnumerator RetrieveRelationshipsCoroutine(string characterId) {
            UnityWebRequest request = RetrieveRelationshipsRequest(characterId);
            yield return request.SendWebRequest();

            if (request.responseCode == 200) {
                RelationshipResponse response = JsonUtility.FromJson<RelationshipResponse>(request.downloadHandler.text);
                OnRelationshipsRetrieved?.Invoke(response?.relationships ?? new List<RelationshipData>());
            } else {
                Debug.LogError($"[ApiManager] RetrieveRelationships failed code={request.responseCode}");
            }
        }

        /// <summary>POST /relationships — records a mutual acquaintance. Server-side
        /// (called when an acquaintance request is accepted).</summary>
        public UnityWebRequest CreateRelationshipRequest(string characterA, string characterB) =>
            BuildJsonRequest($"{this.uri}/relationships", "POST",
                new CreateRelationshipBody { characterA = characterA, characterB = characterB });

        public IEnumerator CreateRelationshipCoroutine(string characterA, string characterB, Action onDone) {
            UnityWebRequest request = CreateRelationshipRequest(characterA, characterB);
            yield return request.SendWebRequest();

            if (request.responseCode != 200 && request.responseCode != 201) {
                Debug.LogError($"[ApiManager] CreateRelationship failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onDone?.Invoke();
        }

        /// <summary>POST /relationships/contact — promotes an acquaintance to contact.
        /// Server-side (called when an "add to contacts" request is accepted).</summary>
        public UnityWebRequest PromoteContactRequest(string characterA, string characterB) =>
            BuildJsonRequest($"{this.uri}/relationships/contact", "POST",
                new CreateRelationshipBody { characterA = characterA, characterB = characterB });

        public IEnumerator PromoteContactCoroutine(string characterA, string characterB, Action onDone) {
            UnityWebRequest request = PromoteContactRequest(characterA, characterB);
            yield return request.SendWebRequest();

            if (request.responseCode != 200 && request.responseCode != 201) {
                Debug.LogError($"[ApiManager] PromoteContact failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onDone?.Invoke();
        }

        /// <summary>DELETE /relationships/:a/:b — full removal of the relationship AND
        /// the whole conversation between the pair. Server-side (called on remove-contact).</summary>
        public UnityWebRequest RemoveContactRequest(string characterA, string characterB) =>
            BuildJsonRequest($"{this.uri}/relationships/{characterA}/{characterB}", "DELETE", null);

        public IEnumerator RemoveContactCoroutine(string characterA, string characterB, Action onDone) {
            UnityWebRequest request = RemoveContactRequest(characterA, characterB);
            yield return request.SendWebRequest();

            if (request.responseCode != 200 && request.responseCode != 204) {
                Debug.LogError($"[ApiManager] RemoveContact failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onDone?.Invoke();
        }

        // ── Vehicles (ownership) ──────────────────────────────────────────────

        /// <summary>Fired after RetrieveOwnedVehicles — the calling character's owned vehicles.</summary>
        public static event Action<List<VehicleData>> OnOwnedVehiclesRetrieved;

        /// <summary>GET /characters/:id/vehicles → fires OnOwnedVehiclesRetrieved (client UI).</summary>
        public void RetrieveOwnedVehicles(string characterId) {
            StartCoroutine(RetrieveOwnedVehiclesCoroutine(characterId,
                list => OnOwnedVehiclesRetrieved?.Invoke(list)));
        }

        /// <summary>GET /characters/:id/vehicles with a callback (server-side validation).</summary>
        public IEnumerator RetrieveOwnedVehiclesCoroutine(string characterId, Action<List<VehicleData>> onDone) {
            UnityWebRequest request = BuildJsonRequest($"{this.uri}/characters/{characterId}/vehicles", "GET", null);
            yield return request.SendWebRequest();

            List<VehicleData> vehicles = new List<VehicleData>();
            if (request.responseCode == 200) {
                VehicleListResponse response = JsonUtility.FromJson<VehicleListResponse>(request.downloadHandler.text);
                if (response?.vehicles != null) vehicles = response.vehicles;
            } else {
                Debug.LogError($"[ApiManager] RetrieveOwnedVehicles failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onDone?.Invoke(vehicles);
        }

        /// <summary>POST /vehicles/ensure — get-or-create the ownership row for a stable
        /// vehicle key. Server-side (called on spawn). Returns the row (owner may be empty).</summary>
        public IEnumerator EnsureVehicleCoroutine(string vehicleKey, string modelId, Action<VehicleData> onDone) {
            UnityWebRequest request = BuildJsonRequest($"{this.uri}/vehicles/ensure", "POST",
                new EnsureVehicleBody { vehicleKey = vehicleKey, modelId = modelId });
            yield return request.SendWebRequest();

            VehicleData vehicle = null;
            if (request.responseCode == 200 || request.responseCode == 201) {
                vehicle = JsonUtility.FromJson<VehicleData>(request.downloadHandler.text);
            } else {
                Debug.LogError($"[ApiManager] EnsureVehicle failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onDone?.Invoke(vehicle);
        }

        /// <summary>POST /places then invokes onPlaceId with the place id (idempotent on placeKey).
        /// Server-side — used to get-or-create a character's virtual garage place.</summary>
        public IEnumerator CreatePlaceCoroutine(CreatePlaceBody body, Action<string> onPlaceId) {
            UnityWebRequest request = CreatePlaceRequest(body);
            yield return request.SendWebRequest();

            string placeId = null;
            if (request.responseCode == 200 || request.responseCode == 201) {
                try {
                    PlaceJson place = JsonConvert.DeserializeObject<PlaceJson>(request.downloadHandler.text);
                    placeId = place?.Id;
                } catch (Exception e) {
                    Debug.LogError($"[ApiManager] CreatePlace parse failed: {e.Message}");
                }
            } else {
                Debug.LogError($"[ApiManager] CreatePlace failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onPlaceId?.Invoke(placeId);
        }

        /// <summary>POST /vehicles — creates a purchased vehicle in the owner's garage. Server-side.</summary>
        public IEnumerator CreateGaragedVehicleCoroutine(string ownerCharacterId, string modelId, string placeId, Action<VehicleData> onDone) {
            UnityWebRequest request = BuildJsonRequest($"{this.uri}/vehicles", "POST",
                new CreateGaragedVehicleBody { ownerCharacterId = ownerCharacterId, modelId = modelId, placeId = placeId });
            yield return request.SendWebRequest();

            VehicleData vehicle = null;
            if (request.responseCode == 200 || request.responseCode == 201) {
                vehicle = JsonUtility.FromJson<VehicleData>(request.downloadHandler.text);
            } else {
                Debug.LogError($"[ApiManager] CreateGaragedVehicle failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onDone?.Invoke(vehicle);
        }

        /// <summary>PATCH /vehicles/:key/owner — claim/transfer ownership. Server-side.</summary>
        public IEnumerator SetVehicleOwnerCoroutine(string vehicleKey, string ownerCharacterId, Action<VehicleData> onDone) {
            UnityWebRequest request = BuildJsonRequest($"{this.uri}/vehicles/{vehicleKey}/owner", "PATCH",
                new SetVehicleOwnerBody { ownerCharacterId = ownerCharacterId });
            yield return request.SendWebRequest();

            VehicleData vehicle = null;
            if (request.responseCode == 200) {
                vehicle = JsonUtility.FromJson<VehicleData>(request.downloadHandler.text);
            } else {
                Debug.LogError($"[ApiManager] SetVehicleOwner failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onDone?.Invoke(vehicle);
        }

        // ── Direct messages (SMS) ─────────────────────────────────────────────

        public static event Action<string, List<DirectMessageData>> OnConversationRetrieved; // (otherId, messages)
        public static event Action<List<UnreadCount>> OnUnreadRetrieved;

        public void RetrieveConversation(string localId, string otherId) {
            StartCoroutine(RetrieveConversationCoroutine(localId, otherId));
        }

        private IEnumerator RetrieveConversationCoroutine(string localId, string otherId) {
            UnityWebRequest request = BuildJsonRequest($"{this.uri}/characters/{localId}/conversations/{otherId}", "GET", null);
            yield return request.SendWebRequest();
            if (request.responseCode == 200) {
                ConversationResponse response = JsonUtility.FromJson<ConversationResponse>(request.downloadHandler.text);
                OnConversationRetrieved?.Invoke(otherId, response?.messages ?? new List<DirectMessageData>());
            } else {
                Debug.LogError($"[ApiManager] RetrieveConversation failed code={request.responseCode}");
            }
        }

        /// <summary>POST /direct-messages — persists an SMS. Server-side.</summary>
        public IEnumerator SendDirectMessageCoroutine(string senderId, string recipientId, string message, Action onDone = null) {
            UnityWebRequest request = BuildJsonRequest($"{this.uri}/direct-messages", "POST",
                new SendDmBody { senderId = senderId, recipientId = recipientId, message = message });
            yield return request.SendWebRequest();
            if (request.responseCode != 200 && request.responseCode != 201) {
                Debug.LogError($"[ApiManager] SendDirectMessage failed code={request.responseCode} body={request.downloadHandler.text}");
            }
            onDone?.Invoke();
        }

        public void MarkConversationRead(string localId, string otherId) {
            StartCoroutine(MarkConversationReadCoroutine(localId, otherId));
        }

        private IEnumerator MarkConversationReadCoroutine(string localId, string otherId) {
            UnityWebRequest request = BuildJsonRequest($"{this.uri}/characters/{localId}/conversations/{otherId}/read", "POST", null);
            yield return request.SendWebRequest();
            if (request.responseCode != 200) {
                Debug.LogError($"[ApiManager] MarkConversationRead failed code={request.responseCode}");
            }
        }

        public void RetrieveUnread(string localId) {
            StartCoroutine(RetrieveUnreadCoroutine(localId));
        }

        private IEnumerator RetrieveUnreadCoroutine(string localId) {
            UnityWebRequest request = BuildJsonRequest($"{this.uri}/characters/{localId}/messages/unread", "GET", null);
            yield return request.SendWebRequest();
            if (request.responseCode == 200) {
                UnreadResponse response = JsonUtility.FromJson<UnreadResponse>(request.downloadHandler.text);
                OnUnreadRetrieved?.Invoke(response?.unread ?? new List<UnreadCount>());
            } else {
                Debug.LogError($"[ApiManager] RetrieveUnread failed code={request.responseCode}");
            }
        }

        public IEnumerator RentTickCoroutine(long nowInGameSeconds, Action<RentTickResponse> onDone) {
            UnityWebRequest request = RentTickRequest(nowInGameSeconds);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success) {
                Debug.LogError($"[ApiManager] RentTick failed code={request.responseCode} body={request.downloadHandler.text}");
                onDone?.Invoke(null);
                yield break;
            }

            RentTickResponse response = JsonConvert.DeserializeObject<RentTickResponse>(request.downloadHandler.text);
            onDone?.Invoke(response);
        }

        // ── Career endpoints ──────────────────────────────────────────────────

        public UnityWebRequest UpdateCharacterCurrentJobRequest(string characterId, CharacterUpdateCurrentJobRequest body) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(body));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/characters/{characterId}/update-current-job", "PUT") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer(),
            };

            request.SetRequestHeader("Content-type", "application/json");

            return request;
        }

        public UnityWebRequest RetrieveCharacterJobsRequest(string characterId) {
            return UnityWebRequest.Get($"{this.uri}/character-jobs/by-character/{characterId}");
        }

        public UnityWebRequest StartCharacterJobRequest(CharacterJobStartRequest body) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(body));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/character-jobs/start", "POST") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer(),
            };

            request.SetRequestHeader("Content-type", "application/json");

            return request;
        }

        // V3 : AddCharacterJobXpRequest retiré — XP supprimé end-to-end (cf. migration 26).

        // ── User Settings endpoints ───────────────────────────────────────────

        // Local cache of the current user's settings. Populated on demand by
        // RetrieveUserSettingsCoroutine, mutated via SaveUserSettings.
        private UserSettings _userSettings;
        public UserSettings UserSettings => _userSettings;
        public static event Action<UserSettings> OnUserSettingsLoaded;

        public UnityWebRequest RetrieveUserSettingsRequest(string userId) {
            return UnityWebRequest.Get($"{this.uri}/user-settings/by-user/{userId}");
        }

        public UnityWebRequest UpdateUserSettingsRequest(string userId, UserSettingsUpdateRequest body) {
            byte[] encodedPayload = new UTF8Encoding().GetBytes(JsonUtility.ToJson(body));

            UnityWebRequest request = new UnityWebRequest($"{this.uri}/user-settings/by-user/{userId}", "PUT") {
                uploadHandler = new UploadHandlerRaw(encodedPayload),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-type", "application/json");
            return request;
        }

        public void LoadUserSettings(string userId) {
            StartCoroutine(LoadUserSettingsCoroutine(userId));
        }

        private IEnumerator LoadUserSettingsCoroutine(string userId) {
            UnityWebRequest req = RetrieveUserSettingsRequest(userId);
            yield return req.SendWebRequest();

            if (req.responseCode == 200) {
                _userSettings = JsonUtility.FromJson<UserSettings>(req.downloadHandler.text);
                if (_userSettings == null) _userSettings = new UserSettings { UserId = userId };
                OnUserSettingsLoaded?.Invoke(_userSettings);
            } else {
                Debug.LogWarning($"[ApiManager] UserSettings load failed: {req.responseCode}");
                _userSettings = new UserSettings { UserId = userId };
                OnUserSettingsLoaded?.Invoke(_userSettings);
            }
        }

        public void SaveUserSettings(UserSettingsData data, Action<bool> onComplete = null) {
            string userId = ResolveLocalUserId();
            if (string.IsNullOrEmpty(userId)) {
                Debug.LogWarning("[ApiManager] SaveUserSettings aborted: no userId (UserSettings not loaded, no authenticated user, no local PlayerController.CharacterData).");
                onComplete?.Invoke(false);
                return;
            }
            StartCoroutine(SaveUserSettingsCoroutine(userId, data, onComplete));
        }

        // Picks the userId from whichever client-side source is available. The
        // normal REST login path populates `this.user`, but the dev shortcuts in
        // SimpleTownNetwork (useSpectus/useElbloody) bypass AuthenticationCoroutine
        // — in that case the only authoritative client-side source of the userId
        // is the locally-owned PlayerController's CharacterData (set via SyncVar).
        public string ResolveLocalUserId() {
            if (_userSettings != null && !string.IsNullOrEmpty(_userSettings.UserId)) return _userSettings.UserId;
            if (!string.IsNullOrEmpty(this.user?.Id)) return this.user.Id;
            var localPlayerObj = Mirror.NetworkClient.localPlayer;
            if (localPlayerObj != null) {
                var controller = localPlayerObj.GetComponent<PlayerController>();
                if (controller != null && controller.CharacterData != null) return controller.CharacterData.UserId;
            }
            return null;
        }

        private IEnumerator SaveUserSettingsCoroutine(string userId, UserSettingsData data, Action<bool> onComplete) {
            UnityWebRequest req = UpdateUserSettingsRequest(userId, new UserSettingsUpdateRequest(data));
            yield return req.SendWebRequest();
            bool ok = req.responseCode == 200;
            if (ok) {
                if (_userSettings == null) _userSettings = new UserSettings { UserId = userId };
                _userSettings.Data = data;
            } else {
                Debug.LogWarning($"[ApiManager] SaveUserSettings failed: status={req.responseCode} result={req.result} error='{req.error}' body='{req.downloadHandler?.text}'");
            }
            onComplete?.Invoke(ok);
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
                    // Fire-and-forget: load user preferences once we know the
                    // user id. SettingsUI and the notif gate read from cache.
                    LoadUserSettings(this.user.Id);
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

        // ── /places, /props, /covers (Phase 1) ──────────────────────────────
        //
        // These build pre-configured UnityWebRequest objects — caller is
        // responsible for `yield return request.SendWebRequest()` and inspecting
        // request.responseCode / request.downloadHandler.text. Bodies use
        // Newtonsoft.Json so nullable fields / dictionaries serialize correctly
        // (JsonUtility can't handle either).

        private UnityWebRequest BuildJsonRequest(string url, string method, object body) {
            UnityWebRequest request = new UnityWebRequest(url, method) {
                downloadHandler = new DownloadHandlerBuffer()
            };
            if (body != null) {
                byte[] payload = new UTF8Encoding().GetBytes(JsonConvert.SerializeObject(body, JsonSettings));
                request.uploadHandler = new UploadHandlerRaw(payload);
            }
            request.SetRequestHeader("Authorization", "Bearer " + this.accessToken);
            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            return request;
        }

        public UnityWebRequest CreatePlaceRequest(CreatePlaceBody body) =>
            BuildJsonRequest($"{this.uri}/places", "POST", body);

        public UnityWebRequest GetPlaceStateRequest(string placeId) =>
            BuildJsonRequest($"{this.uri}/places/{placeId}/state", "GET", null);

        public UnityWebRequest CreatePropRequest(CreatePropBody body) =>
            BuildJsonRequest($"{this.uri}/props", "POST", body);

        public UnityWebRequest GetPropRequest(string propId) =>
            BuildJsonRequest($"{this.uri}/props/{propId}", "GET", null);

        public UnityWebRequest UpdatePropRequest(string propId, UpdatePropBody body) =>
            BuildJsonRequest($"{this.uri}/props/{propId}", "PATCH", body);

        public UnityWebRequest DeletePropRequest(string propId) =>
            BuildJsonRequest($"{this.uri}/props/{propId}", "DELETE", null);

        public UnityWebRequest ListPropsRequest(string placeId, string ownedBy) {
            string qs = "?";
            if (!string.IsNullOrEmpty(placeId)) qs += $"placeId={UnityWebRequest.EscapeURL(placeId)}&";
            if (!string.IsNullOrEmpty(ownedBy)) qs += $"ownedBy={UnityWebRequest.EscapeURL(ownedBy)}";
            return BuildJsonRequest($"{this.uri}/props{qs.TrimEnd('&', '?')}", "GET", null);
        }

        // ── Items ─────────────────────────────────────────────────────────────

        /// <summary>POST /items — insert a new item row (no merge). For pickup
        /// flows prefer UpsertItemRequest which is stack-aware.</summary>
        public UnityWebRequest CreateItemRequest(CreateItemBody body) =>
            BuildJsonRequest($"{this.uri}/items", "POST", body);

        /// <summary>POST /items/upsert — stack-aware add. Increments an existing
        /// matching stack in placeId or inserts a new row.</summary>
        public UnityWebRequest UpsertItemRequest(UpsertItemBody body) =>
            BuildJsonRequest($"{this.uri}/items/upsert", "POST", body);

        /// <summary>PATCH /items/:id — partial update with optimistic locking.</summary>
        public UnityWebRequest UpdateItemRequest(string itemId, UpdateItemBody body) =>
            BuildJsonRequest($"{this.uri}/items/{itemId}", "PATCH", body);

        /// <summary>DELETE /items/:id — destroys the stack (drop / consume).</summary>
        public UnityWebRequest DeleteItemRequest(string itemId) =>
            BuildJsonRequest($"{this.uri}/items/{itemId}", "DELETE", null);

        /// <summary>GET /items?placeId=…&amp;ownedBy=… — at least one filter is required.</summary>
        public UnityWebRequest ListItemsRequest(string placeId, string ownedBy) {
            string qs = "?";
            if (!string.IsNullOrEmpty(placeId)) qs += $"placeId={UnityWebRequest.EscapeURL(placeId)}&";
            if (!string.IsNullOrEmpty(ownedBy)) qs += $"ownedBy={UnityWebRequest.EscapeURL(ownedBy)}";
            return BuildJsonRequest($"{this.uri}/items{qs.TrimEnd('&', '?')}", "GET", null);
        }

        /// <summary>Bulk upsert covers for a place. Body shape: { covers: CoverInputDto[] }.</summary>
        public UnityWebRequest UpsertCoversRequest(string placeId, object body) =>
            BuildJsonRequest($"{this.uri}/places/{placeId}/covers", "PUT", body);

        /// <summary>
        /// One-shot at server boot: ensures the singleton "transit" place exists
        /// and caches its UUID on TransitPlaceId. Subsequent buys POST props with
        /// placeId = TransitPlaceId.
        /// </summary>
        public IEnumerator EnsureTransitPlace() {
            CreatePlaceBody body = new CreatePlaceBody {
                placeKey = "transit",
                type     = "transit",
                ownerId  = "SIMPLE_TOWN"
            };
            UnityWebRequest request = CreatePlaceRequest(body);
            yield return request.SendWebRequest();

            if (request.responseCode != 200 && request.responseCode != 201) {
                Debug.LogError($"[ApiManager] EnsureTransitPlace failed code={request.responseCode} body={request.downloadHandler.text}");
                yield break;
            }

            PlaceJson place = JsonConvert.DeserializeObject<PlaceJson>(request.downloadHandler.text);
            if (place == null || string.IsNullOrEmpty(place.Id)) {
                Debug.LogError($"[ApiManager] EnsureTransitPlace returned invalid payload: {request.downloadHandler.text}");
                yield break;
            }
            this._transitPlaceId = place.Id;
            Debug.Log($"[ApiManager] Transit place ready id={place.Id}");
        }

        /// <summary>
        /// One-shot at server boot: ensures the singleton "city" place exists and
        /// caches its UUID on CityPlaceId. World items dropped on the ground in the
        /// City scene are PATCHed to this place (+ position) so they persist like
        /// apartment world items.
        /// </summary>
        public IEnumerator EnsureCityPlace() {
            CreatePlaceBody body = new CreatePlaceBody {
                placeKey = "city",
                type     = "city",
                ownerId  = "SIMPLE_TOWN"
            };
            UnityWebRequest request = CreatePlaceRequest(body);
            yield return request.SendWebRequest();

            if (request.responseCode != 200 && request.responseCode != 201) {
                Debug.LogError($"[ApiManager] EnsureCityPlace failed code={request.responseCode} body={request.downloadHandler.text}");
                yield break;
            }

            PlaceJson place = JsonConvert.DeserializeObject<PlaceJson>(request.downloadHandler.text);
            if (place == null || string.IsNullOrEmpty(place.Id)) {
                Debug.LogError($"[ApiManager] EnsureCityPlace returned invalid payload: {request.downloadHandler.text}");
                yield break;
            }
            this._cityPlaceId = place.Id;
            Debug.Log($"[ApiManager] City place ready id={place.Id}");
        }
    }
}