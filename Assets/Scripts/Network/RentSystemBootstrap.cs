using Mirror;
using Sim.Entities.Persistence;
using Sim.Logging;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Wires the rent system into Mirror's server lifecycle. Called from
    /// SimpleTownNetwork.OnStartServer / OnStopServer (same pattern as
    /// JobSystemBootstrap). Owns the RentCollectionScheduler GameObject.
    /// </summary>
    public static class RentSystemBootstrap {
        private static GameObject _schedulerGO;

        public static void OnServerStart() {
            _schedulerGO = new GameObject("RentCollectionScheduler");
            Object.DontDestroyOnLoad(_schedulerGO);
            _schedulerGO.AddComponent<RentCollectionScheduler>();
            GameLogger.System.Info("RentSystemServerStarted");
        }

        public static void OnServerStop() {
            if (_schedulerGO != null) {
                Object.Destroy(_schedulerGO);
                _schedulerGO = null;
            }
            GameLogger.System.Info("RentSystemServerStopped");
        }
    }

    /// <summary>
    /// Server-only. Periodically asks the backend to collect rent on every due
    /// home (catch-up cumulative, works for offline tenants too) and react to
    /// evictions. Unlike PlayerCareerSalaryTicker — which only pays online,
    /// connected players — this drives a backend sweep over ALL occupied homes,
    /// so rent is charged regardless of whether the tenant is connected.
    /// </summary>
    public class RentCollectionScheduler : MonoBehaviour {
        // Real-time seconds between collection passes. The backend is the source
        // of truth for due-ness (in-game time), so a coarse cadence is fine: a
        // missed period is recovered on the next pass via catch-up.
        [SerializeField] private float tickIntervalSeconds = 60f;

        private float _accumulator;
        private bool _inFlight;

        private void Update() {
            if (!NetworkServer.active) return;

            _accumulator += Time.deltaTime;
            if (_accumulator < tickIntervalSeconds) return;
            _accumulator = 0f;

            if (_inFlight) return;
            RunTick();
        }

        private void RunTick() {
            ApiManager api = ApiManager.Instance;
            if (api == null) return;

            long now = (long)TimeManager.CurrentTime.TotalSeconds;
            if (now <= 0) return; // clock not hydrated yet

            _inFlight = true;
            // Run on the ApiManager (DontDestroyOnLoad) so an in-flight request
            // survives this scheduler being destroyed on server stop.
            api.StartCoroutine(api.RentTickCoroutine(now, OnTickDone));
        }

        private void OnTickDone(RentTickResponse response) {
            _inFlight = false;
            if (response == null) return;

            int evictionCount = response.evictions?.Count ?? 0;
            if (evictionCount > 0) {
                foreach (RentEvictionDto eviction in response.evictions) {
                    HandleEviction(eviction);
                }
            }

            if (response.charges != null) {
                foreach (RentChargeDto charge in response.charges) {
                    HandleCharge(charge);
                }
            }

            if (response.paid > 0 || evictionCount > 0) {
                GameLogger.System.Info("RentTick paid={Paid} evicted={Evicted}", response.paid, evictionCount);
            }
        }

        // collect_rent mutated characters.money in the DB directly, so refresh the
        // money SyncVar of any affected online player. The player-owner who just
        // received rent also gets a toast; the debited tenant is refreshed
        // silently (their own debit feedback is not requested).
        private void HandleCharge(RentChargeDto charge) {
            if (charge == null) return;

            RefreshBalance(charge.tenantId, charge.tenantBalance);

            if (!string.IsNullOrEmpty(charge.ownerId) && charge.amount > 0) {
                NetworkConnectionToClient ownerConn = RefreshBalance(charge.ownerId, charge.ownerBalance);
                ownerConn?.Send(new ToastNotificationMessage {
                    text = $"Loyer reçu : +{charge.amount} €",
                    typeByte = (byte)NotificationType.BANK,
                });
            }
        }

        // Returns the connection if the character is online (so callers can also
        // message it), or null otherwise.
        private static NetworkConnectionToClient RefreshBalance(string characterId, int authoritativeBalance) {
            NetworkConnectionToClient conn = FindConnectionByCharacter(characterId);
            if (conn?.identity == null) return null;

            PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
            bank?.SetAuthoritativeBalance(authoritativeBalance);
            return conn;
        }

        // Online tenant: physically remove them from the apartment and notify.
        // Offline tenant: nothing to do — the backend already vacated the home
        // and moved their belongings to a transit place.
        private void HandleEviction(RentEvictionDto eviction) {
            if (eviction == null || string.IsNullOrEmpty(eviction.characterId)) return;

            NetworkConnectionToClient conn = FindConnectionByCharacter(eviction.characterId);
            if (conn == null) return;

            SimpleTownNetwork net = NetworkManager.singleton as SimpleTownNetwork;
            net?.ServerTeleportToCity(conn);

            conn.Send(new ToastNotificationMessage {
                text = "Vous avez été expulsé de votre logement pour loyer impayé.",
                typeByte = (byte)NotificationType.BANK,
            });
        }

        private static NetworkConnectionToClient FindConnectionByCharacter(string characterId) {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn?.identity == null) continue;
                PlayerController player = conn.identity.GetComponent<PlayerController>();
                if (player?.CharacterData != null && player.CharacterData.Id == characterId) {
                    return conn;
                }
            }
            return null;
        }
    }
}
