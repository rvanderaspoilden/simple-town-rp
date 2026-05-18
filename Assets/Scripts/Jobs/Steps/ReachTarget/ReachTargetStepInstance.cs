using UnityEngine;

namespace Sim.Jobs {
    public sealed class ReachTargetStepInstance : JobStepInstance {
        private readonly ReachTargetStepDefinition def;
        private readonly float radiusSqr;

        public ReachTargetStepInstance(JobInstance job, ReachTargetStepDefinition definition) : base(job) {
            def = definition;
            radiusSqr = definition.ArrivalRadius * definition.ArrivalRadius;
        }

        public override void OnEnter() {
            var target = job.Context.TargetByKey(def.TargetKey);
            if (target == null || !target.IsAvailable) {
                Fail(JobFailureReason.TargetLost);
                return;
            }
            job.Context.waypoint = target.Transform.position;
        }

        public override void Tick(float dt) {
            var target = job.Context.TargetByKey(def.TargetKey);
            if (target == null || !target.IsAvailable) {
                Fail(JobFailureReason.TargetLost);
                return;
            }

            var ownerTarget = JobTargetRegistry.Instance.GetPlayer(job.OwnerNetId);
            if (ownerTarget == null || !ownerTarget.IsAvailable) {
                Fail(JobFailureReason.OwnerDisconnected);
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
