using Sim.Logging;

namespace Sim.Missions {
    /// <summary>
    /// Souscrit aux events MissionEvents.MissionFailed et despawn proprement l'item
    /// associé à la mission s'il a été stocké dans le MissionContext (clés
    /// PickupPackageStepInstance.CtxEntityIdKey / CtxRoomIdKey).
    ///
    /// Couvre les cas : Failed (TargetLost/OwnerDisconnected/MinigameFailed),
    /// Abandoned (joueur clique "Abandonner"), Expired (timeout mission).
    /// Le cas Completed ne passe PAS par ici — c'est DeliverToTargetStep qui
    /// despawn proprement à la livraison.
    /// </summary>
    public static class MissionItemCleanup {
        private static bool _subscribed;

        public static void Subscribe() {
            if (_subscribed) return;
            MissionEvents.MissionFailed += OnMissionFailed;
            _subscribed = true;
        }

        public static void Unsubscribe() {
            if (!_subscribed) return;
            MissionEvents.MissionFailed -= OnMissionFailed;
            _subscribed = false;
        }

        private static void OnMissionFailed(MissionInstance job) {
            if (job?.Context == null) return;
            if (!job.Context.TryGetStruct<int>(PickupPackageStepInstance.CtxEntityIdKey, out var entityId)) return;
            var roomId = job.Context.Get<string>(PickupPackageStepInstance.CtxRoomIdKey) ?? "city";

            ServerItemManager.Instance.DespawnItem(roomId, entityId);
            GameLogger.System.Info("MissionItemCleanup_Despawned {EntityId} {RoomId} {MissionId} {Reason}",
                entityId, roomId, job.Definition.MissionId, job.FailureReason);
        }
    }
}
