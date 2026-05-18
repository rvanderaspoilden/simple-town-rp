using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Provider de debug pour le métier Livreur.
    ///   publishKey → Publish sur le board (Available, prenable par tous)
    ///
    /// Tirage : un pickup point (Role=Pickup) + un delivery point (Role=Delivery)
    /// parmi les JobPoint actifs dans la scène et compatibles avec la
    /// catégorie de la mission.
    /// </summary>
    public class JobsDebugProvider : MonoBehaviour {
        [Tooltip("Définition de mission à publier.")]
        [SerializeField] private JobDefinition jobDefinition;

        [Tooltip("Touche de publication sur le board (Available).")]
        [SerializeField] private KeyCode publishKey = KeyCode.F10;

        private void Update() {
            if (!NetworkServer.active) return;
            if (Input.GetKeyDown(publishKey)) PublishOnBoard();
        }

        public void PublishOnBoard() {
            if (jobDefinition == null) {
                GameLogger.System.Warning("JobsDebugProvider_NoDefinition");
                return;
            }
            if (!TryBuildContext(out var ctx, out var pickup, out var delivery)) return;

            var job = JobServerManager.Instance.Publish(jobDefinition, ctx);
            if (job != null) {
                GameLogger.System.Info("JobsDebugProvider_Published {JobId} {Category} {Pickup} {Delivery}",
                    jobDefinition.JobId, jobDefinition.Category, pickup.TargetId, delivery.TargetId);
            }
        }

        private bool TryBuildContext(out JobContext ctx, out IJobTarget pickup, out IJobTarget delivery) {
            ctx = null;
            pickup = null;
            delivery = null;

            pickup = PickRandomPoint(jobDefinition.Category, PointRole.Pickup, except: null);
            if (pickup == null) {
                GameLogger.System.Warning("JobsDebugProvider_NoPickup {Category}", jobDefinition.Category);
                return false;
            }

            delivery = PickRandomPoint(jobDefinition.Category, PointRole.Delivery, except: pickup);
            if (delivery == null) {
                GameLogger.System.Warning("JobsDebugProvider_NoDelivery {Category}", jobDefinition.Category);
                return false;
            }

            ctx = new JobContext();
            ctx.SetTarget(JobTargetKey.Pickup, pickup);
            ctx.SetTarget(JobTargetKey.Delivery, delivery);
            return true;
        }

        private static IJobTarget PickRandomPoint(JobCategory category, PointRole role, IJobTarget except) {
            var candidates = new List<JobPoint>();
            foreach (var p in JobPoint.ByPointId.Values) {
                if (p == null || !p.IsAvailable) continue;
                if (p.Category != category) continue;
                if (!p.MatchesRole(role)) continue;
                if (except != null && p.TargetId == except.TargetId) continue;
                candidates.Add(p);
            }
            if (candidates.Count == 0) return null;
            return candidates[Random.Range(0, candidates.Count)];
        }
    }
}
