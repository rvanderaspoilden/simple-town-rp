namespace Sim.Jobs {
    public sealed class DeliverToTargetStepInstance : JobStepInstance {
        private readonly DeliverToTargetStepDefinition def;
        private readonly float radiusSqr;
        private float inRangeElapsed;

        public DeliverToTargetStepInstance(JobInstance job, DeliverToTargetStepDefinition definition) : base(job) {
            def = definition;
            radiusSqr = definition.InteractionRadius * definition.InteractionRadius;
        }

        public override void OnEnter() {
            inRangeElapsed = 0f;
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

            var owner = JobTargetRegistry.Instance.GetPlayer(job.OwnerNetId);
            if (owner == null || !owner.IsAvailable) {
                Fail(JobFailureReason.OwnerDisconnected);
                return;
            }

            job.Context.waypoint = target.Transform.position;

            var delta = owner.Transform.position - target.Transform.position;
            if (delta.sqrMagnitude <= radiusSqr) {
                inRangeElapsed += dt;
                if (inRangeElapsed >= def.HandoverSeconds) Succeed();
            } else {
                inRangeElapsed = 0f;
            }
        }

        public override void OnExit() {
            job.Context.waypoint = null;
        }
    }
}
