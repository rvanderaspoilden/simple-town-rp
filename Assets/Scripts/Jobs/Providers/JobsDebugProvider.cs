using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Provider de debug. Drop ce MonoBehaviour dans une scène (serveur).
    ///   offerKey   → Offer direct au premier joueur connecté (mission Offered)
    ///   publishKey → Publish sur le board (mission Available, prenable par tous)
    /// Une cible primaire est choisie au hasard parmi les JobDeliveryPoint
    /// présents en scène et compatibles avec la catégorie de la mission.
    /// </summary>
    public class JobsDebugProvider : MonoBehaviour {
        [Tooltip("Définition de mission à offrir / publier.")]
        [SerializeField] private JobDefinition jobDefinition;

        [Tooltip("Touche d'offre directe au premier joueur (Offered).")]
        [SerializeField] private KeyCode offerKey = KeyCode.F9;

        [Tooltip("Touche de publication sur le board (Available).")]
        [SerializeField] private KeyCode publishKey = KeyCode.F10;

        private void Update() {
            if (!NetworkServer.active) return;
            if (Input.GetKeyDown(offerKey)) OfferToFirstPlayer();
            else if (Input.GetKeyDown(publishKey)) PublishOnBoard();
        }

        public void OfferToFirstPlayer() {
            if (jobDefinition == null) {
                GameLogger.System.Warning("JobsDebugProvider_NoDefinition");
                return;
            }
            var owner = FindFirstPlayerNetId();
            if (owner == 0u) {
                GameLogger.System.Warning("JobsDebugProvider_NoPlayer");
                return;
            }
            if (!TryBuildContext(out var ctx, out var primary)) return;

            var job = JobServerManager.Instance.Offer(jobDefinition, owner, ctx);
            if (job != null) {
                GameLogger.System.Info("JobsDebugProvider_Offered {JobId} {NetId} {TargetId}",
                    jobDefinition.JobId, owner, primary?.TargetId ?? "<none>");
            }
        }

        public void PublishOnBoard() {
            if (jobDefinition == null) {
                GameLogger.System.Warning("JobsDebugProvider_NoDefinition");
                return;
            }
            if (!TryBuildContext(out var ctx, out var primary)) return;

            var job = JobServerManager.Instance.Publish(jobDefinition, ctx);
            if (job != null) {
                GameLogger.System.Info("JobsDebugProvider_Published {JobId} {Category} {TargetId}",
                    jobDefinition.JobId, jobDefinition.Category, primary?.TargetId ?? "<none>");
            }
        }

        private bool TryBuildContext(out JobContext ctx, out IJobTarget primary) {
            primary = null;
            ctx = null;

            var candidates = new List<JobDeliveryPoint>();
            foreach (var p in JobDeliveryPoint.ByPointId.Values) {
                if (p == null || !p.IsAvailable) continue;
                if (p.Category != jobDefinition.Category) continue;
                candidates.Add(p);
            }
            if (candidates.Count < 2) {
                GameLogger.System.Warning("JobsDebugProvider_NotEnoughDeliveryPoints {Category} {Count}",
                    jobDefinition.Category, candidates.Count);
                return false;
            }

            int pickupIdx = Random.Range(0, candidates.Count);
            int deliveryIdx;
            do { deliveryIdx = Random.Range(0, candidates.Count); }
            while (deliveryIdx == pickupIdx);

            primary = candidates[pickupIdx];
            var secondary = candidates[deliveryIdx];
            ctx = new JobContext { primaryTarget = primary, secondaryTarget = secondary };
            return true;
        }

        private static uint FindFirstPlayerNetId() {
            foreach (var conn in NetworkServer.connections.Values) {
                if (conn?.identity == null) continue;
                return conn.identity.netId;
            }
            return 0u;
        }
    }
}
