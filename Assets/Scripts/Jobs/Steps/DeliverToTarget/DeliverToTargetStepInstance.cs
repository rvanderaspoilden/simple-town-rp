namespace Sim.Jobs {
    /// <summary>
    /// Runtime du DeliverToTargetStep. Le step succeed après handoverSeconds
    /// consécutives à portée du target (existant). Nouveau : si le context
    /// contient une entityId de colis (déposée par PickupPackageStep), on
    /// vérifie que le owner la détient effectivement en main avant
    /// d'incrémenter le compteur — sortir du rayon OU lâcher le colis
    /// réinitialise le timer.
    ///
    /// À la complétion, le colis (s'il existe) est despawn silencieusement
    /// (drop simulé). Cleanup en cas d'échec géré par JobItemCleanup.
    /// </summary>
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

            bool inRange = (owner.Transform.position - target.Transform.position).sqrMagnitude <= radiusSqr;
            bool holdsPackage = OwnerHoldsPackage();

            if (inRange && holdsPackage) {
                inRangeElapsed += dt;
                if (inRangeElapsed >= def.HandoverSeconds) {
                    DespawnPackage();
                    Succeed();
                }
            } else {
                inRangeElapsed = 0f;
            }
        }

        public override void OnExit() {
            job.Context.waypoint = null;
        }

        /// <summary>
        /// Si la mission ne porte pas de colis (ex. job sans PickupPackageStep),
        /// considérer que la condition "tient le colis" est satisfaite — le step
        /// reste un simple handover par proximité comme avant.
        /// </summary>
        private bool OwnerHoldsPackage() {
            if (!job.Context.TryGetStruct<int>(PickupPackageStepInstance.CtxEntityIdKey, out var entityId)) return true;
            var roomId = job.Context.Get<string>(PickupPackageStepInstance.CtxRoomIdKey) ?? "city";

            var entity = ServerItemManager.Instance.GetEntity(roomId, entityId);
            return entity != null && entity.HolderNetId == job.OwnerNetId;
        }

        private void DespawnPackage() {
            if (!job.Context.TryGetStruct<int>(PickupPackageStepInstance.CtxEntityIdKey, out var entityId)) return;
            var roomId = job.Context.Get<string>(PickupPackageStepInstance.CtxRoomIdKey) ?? "city";

            ServerItemManager.Instance.DespawnItem(roomId, entityId);
        }
    }
}
