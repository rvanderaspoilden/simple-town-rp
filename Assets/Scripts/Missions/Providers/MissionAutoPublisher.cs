using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Spawne automatiquement des offres de mission sur le board à intervalle
    /// aléatoire. Borne le nombre d'offres "Available" simultanées : tant que
    /// le cap est atteint, aucune nouvelle offre n'apparaît. Côté définition,
    /// la durée d'affichage d'une offre est gérée par
    /// MissionDefinition.BoardExpirationSeconds (tick côté serveur).
    /// </summary>
    public class MissionAutoPublisher : MonoBehaviour {
        [Tooltip("Définitions de mission à publier (tirage uniforme à chaque arrivage).")]
        [SerializeField] private List<MissionDefinition> jobDefinitions = new List<MissionDefinition>();

        [Tooltip("Intervalle minimum entre deux arrivages (secondes).")]
        [SerializeField] private float minInterval = 30f;

        [Tooltip("Intervalle maximum entre deux arrivages (secondes).")]
        [SerializeField] private float maxInterval = 90f;

        [Tooltip("Nombre maximum d'offres Available simultanées sur le board (toutes catégories confondues).")]
        [SerializeField] private int maxAvailableOffers = 5;

        [Tooltip("Délai avant la première tentative d'arrivage (secondes).")]
        [SerializeField] private float startDelay = 10f;

        [Tooltip("Si le cap est atteint, prochaine vérification après ce délai (secondes).")]
        [SerializeField] private float retryInterval = 5f;

        private float _nextAttemptTime;
        private bool _scheduled;

        private void OnEnable() {
            _scheduled = false;
        }

        private void Update() {
            if (!NetworkServer.active) return;

            if (!_scheduled) {
                _nextAttemptTime = Time.time + Mathf.Max(0f, startDelay);
                _scheduled = true;
            }

            if (Time.time < _nextAttemptTime) return;

            if (CountAvailableOffers() >= maxAvailableOffers) {
                _nextAttemptTime = Time.time + Mathf.Max(0.1f, retryInterval);
                return;
            }

            TryPublishOne();
            _nextAttemptTime = Time.time + RandomInterval();
        }

        private float RandomInterval() {
            float lo = Mathf.Max(0.1f, minInterval);
            float hi = Mathf.Max(lo, maxInterval);
            return Random.Range(lo, hi);
        }

        private int CountAvailableOffers() {
            int n = 0;
            foreach (var job in MissionServerManager.Instance.Active) {
                if (job.Status == MissionStatus.Available) n++;
            }
            return n;
        }

        private void TryPublishOne() {
            if (jobDefinitions.Count == 0) {
                GameLogger.System.Warning("MissionAutoPublisher_NoDefinitions");
                return;
            }
            var def = jobDefinitions[Random.Range(0, jobDefinitions.Count)];
            if (def == null) return;

            if (!TryBuildContext(def, out var ctx, out var pickup, out var delivery)) return;

            var job = MissionServerManager.Instance.Publish(def, ctx);
            if (job != null) {
                GameLogger.System.Info("MissionAutoPublisher_Published {MissionId} {Category} {Pickup} {Delivery}",
                    def.MissionId, def.ProfessionId, pickup.TargetId, delivery.TargetId);
            }
        }

        private static bool TryBuildContext(MissionDefinition def, out MissionContext ctx, out IMissionTarget pickup, out IMissionTarget delivery) {
            ctx = null;
            pickup = PickRandomPoint(def.ProfessionId, PointRole.Pickup, except: null);
            delivery = null;
            if (pickup == null) {
                GameLogger.System.Warning("MissionAutoPublisher_NoPickup {Category}", def.ProfessionId);
                return false;
            }

            delivery = PickRandomPoint(def.ProfessionId, PointRole.Delivery, except: pickup);
            if (delivery == null) {
                GameLogger.System.Warning("MissionAutoPublisher_NoDelivery {Category}", def.ProfessionId);
                return false;
            }

            ctx = new MissionContext();
            ctx.SetTarget(MissionTargetKey.Pickup, pickup);
            ctx.SetTarget(MissionTargetKey.Delivery, delivery);
            return true;
        }

        private static IMissionTarget PickRandomPoint(string professionId, PointRole role, IMissionTarget except) {
            var candidates = new List<MissionPoint>();
            foreach (var p in MissionPoint.ByPointId.Values) {
                if (p == null || !p.IsAvailable) continue;
                if (p.ProfessionId != professionId) continue;
                if (!p.MatchesRole(role)) continue;
                if (except != null && p.TargetId == except.TargetId) continue;
                candidates.Add(p);
            }
            if (candidates.Count == 0) return null;
            return candidates[Random.Range(0, candidates.Count)];
        }
    }
}
