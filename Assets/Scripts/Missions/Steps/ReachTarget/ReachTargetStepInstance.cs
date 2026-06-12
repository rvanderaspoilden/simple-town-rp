using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    public sealed class ReachTargetStepInstance : MissionStepInstance {
        private readonly ReachTargetStepDefinition def;
        private readonly float radiusSqr;

        public ReachTargetStepInstance(MissionInstance job, ReachTargetStepDefinition definition) : base(job) {
            def = definition;
            radiusSqr = definition.ArrivalRadius * definition.ArrivalRadius;
        }

        public override void OnEnter() {
            // Override optionnel : si la def fixe un pointId précis, on écrase le target
            // du MissionContext sous la même clé — les steps suivants utilisant la même
            // TargetKey hériteront aussi de cet override (ex. ReachPickup → PickupPackage).
            if (!string.IsNullOrEmpty(def.OverrideTargetPointId)) {
                if (MissionPoint.ByPointId.TryGetValue(def.OverrideTargetPointId, out var p) && p != null) {
                    job.Context.SetTarget(def.TargetKey, p);
                } else {
                    GameLogger.System.Warning("ReachTargetStep_OverrideNotFound {PointId} {MissionId}",
                        def.OverrideTargetPointId, job.Definition.MissionId);
                }
            }

            var target = job.Context.TargetByKey(def.TargetKey);
            if (target == null || !target.IsAvailable) {
                Fail(MissionFailureReason.TargetLost);
                return;
            }
            job.Context.waypoint = target.Transform.position;
        }

        public override void Tick(float dt) {
            var target = job.Context.TargetByKey(def.TargetKey);
            if (target == null || !target.IsAvailable) {
                Fail(MissionFailureReason.TargetLost);
                return;
            }

            var ownerTarget = MissionTargetRegistry.Instance.GetPlayer(job.OwnerNetId);
            if (ownerTarget == null || !ownerTarget.IsAvailable) {
                Fail(MissionFailureReason.OwnerDisconnected);
                return;
            }

            // Cible mobile (joueur/PNJ) : on rafraîchit le waypoint pour le HUD.
            job.Context.waypoint = target.Transform.position;

            var delta = ownerTarget.Transform.position - target.Transform.position;
            if (delta.sqrMagnitude <= radiusSqr) Succeed();
        }

        public override void OnExit() {
            job.Context.waypoint = null;
        }
    }
}
