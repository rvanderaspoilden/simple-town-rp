using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Provider de debug pour le métier Livreur.
    ///   publishKey → Publish sur le board (Available, prenable par tous)
    ///
    /// Tirage : un pickup point (Role=Pickup) + un delivery point (Role=Delivery)
    /// parmi les MissionPoint actifs dans la scène et compatibles avec la
    /// catégorie de la mission.
    /// </summary>
    public class MissionsDebugProvider : MonoBehaviour {
        [Tooltip("Définition de mission à publier.")]
        [SerializeField] private MissionDefinition jobDefinition;

        [Tooltip("Touche de publication sur le board (Available).")]
        [SerializeField] private KeyCode publishKey = KeyCode.F10;

        private void Update() {
            if (!NetworkServer.active) return;
            if (Input.GetKeyDown(publishKey)) PublishOnBoard();
        }

        public void PublishOnBoard() {
            if (jobDefinition == null) {
                GameLogger.System.Warning("MissionsDebugProvider_NoDefinition");
                return;
            }
            if (!TryBuildContext(out var ctx, out var pickup, out var delivery)) return;

            var job = MissionServerManager.Instance.Publish(jobDefinition, ctx);
            if (job != null) {
                GameLogger.System.Info("MissionsDebugProvider_Published {MissionId} {Category} {Pickup} {Delivery}",
                    jobDefinition.MissionId, jobDefinition.ProfessionId, pickup.TargetId, delivery.TargetId);
            }
        }

        private bool TryBuildContext(out MissionContext ctx, out IMissionTarget pickup, out IMissionTarget delivery) {
            ctx = null;
            pickup = null;
            delivery = null;

            pickup = PickRandomPoint(jobDefinition.ProfessionId, PointRole.Pickup, except: null);
            if (pickup == null) {
                GameLogger.System.Warning("MissionsDebugProvider_NoPickup {Category}", jobDefinition.ProfessionId);
                return false;
            }

            delivery = PickRandomPoint(jobDefinition.ProfessionId, PointRole.Delivery, except: pickup);
            if (delivery == null) {
                GameLogger.System.Warning("MissionsDebugProvider_NoDelivery {Category}", jobDefinition.ProfessionId);
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
