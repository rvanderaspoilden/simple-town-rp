#if STRESS_TEST_BOTS
using System;
using System.Collections;
using System.Text;
using Mirror;
using Sim.Entities;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;

namespace Sim.StressTest {
    /// <summary>
    /// Headless stress-test driver. When the process is launched with --bot, this
    /// component takes control of the boot sequence (LauncherManager defers to
    /// it), provisions an account against POST /auth/register-bot, connects to
    /// the Mirror server, and once the local PlayerController exists, drives a
    /// random NavMesh walk loop forever. Reconnects automatically on disconnect.
    ///
    /// Single instance per process — Mirror's NetworkClient is static so you
    /// cannot host multiple bots in one process. Spawn N processes via
    /// tools/stress-test/launch-bots.ps1 instead.
    /// </summary>
    public class BotRunner : MonoBehaviour {

        /// <summary>
        /// Spawned automatically at process start when --bot is on the command
        /// line. Avoids requiring a GameObject in every scene that might be
        /// loaded first, and keeps the regular dev/main builds untouched.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap() {
            if (!CommandLineArgs.BotMode) return;
            var go = new GameObject("[BotRunner]");
            go.AddComponent<BotRunner>();
            // BotRunner.Awake calls DontDestroyOnLoad
        }

        [SerializeField] private float minWaitBetweenMoves = 3f;
        [SerializeField] private float maxWaitBetweenMoves = 8f;
        [SerializeField] private float wanderRadius = 15f;
        [SerializeField] private float reconnectDelay = 5f;

        private bool _running;
        private bool _moveLoopStarted;

        private void Awake() {
            if (!CommandLineArgs.BotMode) {
                Destroy(this.gameObject);
                return;
            }
            DontDestroyOnLoad(this.gameObject);
            Application.targetFrameRate = 30;     // bots don't render - cap CPU
            QualitySettings.vSyncCount = 0;
            QualitySettings.SetQualityLevel(0, applyExpensiveChanges: false);
            AudioListener.volume = 0f;            // skip audio decoding/clipping
            AudioListener.pause = true;
            Debug.Log($"[Bot] Booted as bot index={CommandLineArgs.BotIndex} server={CommandLineArgs.BotServer}");
        }

        private void Start() {
            if (!CommandLineArgs.BotMode) return;
            this._running = true;
            StartCoroutine(this.RunCoroutine());
        }

        private void OnDestroy() {
            this._running = false;
        }

        // ── main loop ────────────────────────────────────────────────────────

        private IEnumerator RunCoroutine() {
            while (this._running) {
                yield return this.WaitForApiManager();
                yield return this.RegisterAndConnect();

                // Wait until either the local player spawns or the client is
                // disconnected. Whichever happens first.
                while (this._running && NetworkClient.isConnected) {
                    if (!this._moveLoopStarted && PlayerController.Local != null) {
                        this._moveLoopStarted = true;
                        // Bot already spawned in the city (CreateCharacterMessage.spawnInCity=true);
                        // wander loop can start directly, no elevator hop needed.
                        StartCoroutine(this.MoveLoop());
                    }
                    yield return new WaitForSeconds(0.5f);
                }

                this._moveLoopStarted = false;
                Debug.LogWarning($"[Bot] Disconnected — reconnecting in {this.reconnectDelay}s");
                yield return new WaitForSeconds(this.reconnectDelay);
            }
        }

        private IEnumerator WaitForApiManager() {
            while (ApiManager.Instance == null || NetworkManager.singleton == null) {
                yield return null;
            }
        }

        private IEnumerator RegisterAndConnect() {
            int index = CommandLineArgs.BotIndex;
            string secret = CommandLineArgs.BotSecret ?? string.Empty;
            string url = $"{ApiManager.Instance.Uri}/auth/register-bot";
            string payload = JsonUtility.ToJson(new RegisterBotRequest { botIndex = index });

            using var req = new UnityWebRequest(url, "POST") {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 30,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("x-bot-secret", secret);

            yield return req.SendWebRequest();

            if (req.responseCode != 200 && req.responseCode != 201) {
                Debug.LogError($"[Bot] register-bot failed: status={req.responseCode} body={req.downloadHandler?.text}");
                yield break;
            }

            RegisterBotResponse resp;
            try {
                resp = JsonUtility.FromJson<RegisterBotResponse>(req.downloadHandler.text);
            } catch (Exception e) {
                Debug.LogError($"[Bot] register-bot bad JSON: {e.Message} body={req.downloadHandler?.text}");
                yield break;
            }

            ApiManager.Instance.SetBotAuth(resp.access_token, new User {
                Id = resp.userId,
                Username = resp.username,
                Email = $"{resp.username}@stress.local",
            });

            // Pull the full CharacterData (the bot endpoint only returned ids).
            CharacterData characterData = null;
            using var charReq = UnityWebRequest.Get($"{ApiManager.Instance.Uri}/characters/{resp.characterId}");
            charReq.timeout = 30;
            yield return charReq.SendWebRequest();
            if (charReq.responseCode == 200) {
                var wrapper = JsonUtility.FromJson<CharacterResponse>(charReq.downloadHandler.text);
                if (wrapper?.Characters != null && wrapper.Characters.Length > 0) {
                    characterData = wrapper.Characters[0];
                }
            }
            if (characterData == null) {
                Debug.LogError($"[Bot] failed to load character {resp.characterId}");
                yield break;
            }

            var net = (SimpleTownNetwork) NetworkManager.singleton;
            net.CharacterData = characterData;
            // Homes are not strictly needed for the connect handshake, but the
            // server expects RetrieveHomesByCharacter to have populated them by
            // the time UpdateCityDataMessage is processed. Skip the event-based
            // flow and do it inline.
            yield return this.LoadHomes(characterData);

            Debug.Log($"[Bot] Connecting bot={resp.username} char={resp.characterId} → {net.networkAddress}");
            net.StartClient();
        }

        private IEnumerator LoadHomes(CharacterData data) {
            using var req = UnityWebRequest.Get($"{ApiManager.Instance.Uri}/characters/{data.Id}/homes");
            req.timeout = 30;
            yield return req.SendWebRequest();
            if (req.responseCode == 200) {
                var resp = JsonUtility.FromJson<HomeResponse>(req.downloadHandler.text);
                if (resp?.Homes != null) {
                    ((SimpleTownNetwork) NetworkManager.singleton).CharacterHomes = new System.Collections.Generic.List<Home>(resp.Homes);
                }
            } else {
                Debug.LogWarning($"[Bot] loadHomes status={req.responseCode}");
            }
        }

        // ── wander ────────────────────────────────────────────────────────────

        private IEnumerator MoveLoop() {
            Debug.Log("[Bot] Local player ready — starting move loop");
            while (this._running && NetworkClient.isConnected && PlayerController.Local != null) {
                Vector3 origin = PlayerController.Local.transform.position;
                if (this.TrySampleNavMesh(origin, this.wanderRadius, out Vector3 target)) {
                    bool running = UnityEngine.Random.value > 0.5f;
                    PlayerController.Local.MoveTo(target, running);
                }
                float wait = UnityEngine.Random.Range(this.minWaitBetweenMoves, this.maxWaitBetweenMoves);
                yield return new WaitForSeconds(wait);
            }
        }

        private bool TrySampleNavMesh(Vector3 origin, float radius, out Vector3 result) {
            for (int i = 0; i < 5; i++) {
                Vector2 disk = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 candidate = origin + new Vector3(disk.x, 0f, disk.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) {
                    result = hit.position;
                    return true;
                }
            }
            result = origin;
            return false;
        }

        // ── DTOs ──────────────────────────────────────────────────────────────

        [Serializable]
        private struct RegisterBotRequest {
            public int botIndex;
        }

        [Serializable]
        private class RegisterBotResponse {
            public string access_token;
            public string userId;
            public string characterId;
            public string username;
        }
    }
}
#endif
