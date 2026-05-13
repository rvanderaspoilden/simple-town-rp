using Sim.Logging;

namespace Sim.Jobs {
    /// <summary>
    /// Souscrit aux events JobEvents.JobFailed et despawn proprement l'item
    /// associé à la mission s'il a été stocké dans le JobContext (clés
    /// PickupPackageStepInstance.CtxEntityIdKey / CtxRoomIdKey).
    ///
    /// Couvre les cas : Failed (TargetLost/OwnerDisconnected/MinigameFailed),
    /// Abandoned (joueur clique "Abandonner"), Expired (timeout mission).
    /// Le cas Completed ne passe PAS par ici — c'est DeliverToTargetStep qui
    /// despawn proprement à la livraison.
    /// </summary>
    public static class JobItemCleanup {
        private static bool _subscribed;

        public static void Subscribe() {
            if (_subscribed) return;
            JobEvents.JobFailed += OnJobFailed;
            _subscribed = true;
        }

        public static void Unsubscribe() {
            if (!_subscribed) return;
            JobEvents.JobFailed -= OnJobFailed;
            _subscribed = false;
        }

        private static void OnJobFailed(JobInstance job) {
            if (job?.Context == null) return;
            if (!job.Context.TryGetStruct<int>(PickupPackageStepInstance.CtxEntityIdKey, out var entityId)) return;
            var roomId = job.Context.Get<string>(PickupPackageStepInstance.CtxRoomIdKey) ?? "city";

            ServerItemManager.Instance.DespawnItem(roomId, entityId);
            GameLogger.System.Info("JobItemCleanup_Despawned {EntityId} {RoomId} {JobId} {Reason}",
                entityId, roomId, job.Definition.JobId, job.FailureReason);
        }
    }
}
