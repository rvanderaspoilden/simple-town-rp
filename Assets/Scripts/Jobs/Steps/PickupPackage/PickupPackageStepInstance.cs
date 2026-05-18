using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Runtime du PickupPackageStep. Réutilise ServerItemManager :
    ///   OnEnter : SpawnItem au pickup point, stocke entityId/roomId dans context
    ///   Tick    : poll l'ItemEntity ; succeed quand le owner le tient en main
    ///             (le système Items existant gère le PICK action + l'attache à la main)
    ///   OnExit  : si statut != Succeeded → DespawnItem (cleanup avant pickup)
    /// </summary>
    public sealed class PickupPackageStepInstance : JobStepInstance {
        public const string CtxEntityIdKey = "packageEntityId";
        public const string CtxRoomIdKey   = "packageRoomId";

        private readonly PickupPackageStepDefinition def;
        private int _spawnedEntityId = -1;

        public PickupPackageStepInstance(JobInstance job, PickupPackageStepDefinition definition) : base(job) {
            def = definition;
        }

        public override void OnEnter() {
            if (def.ItemConfig == null || def.ItemConfig.ID <= 0) {
                GameLogger.System.Error("PickupPackageStep_InvalidItemConfig {JobId}", job.Definition.JobId);
                Fail(JobFailureReason.None);
                return;
            }

            var target = job.Context.TargetByKey(def.SpawnAtKey);
            if (target == null || !target.IsAvailable) {
                Fail(JobFailureReason.TargetLost);
                return;
            }

            var pos = target.Transform.position + def.SpawnOffset;
            _spawnedEntityId = ServerItemManager.Instance.SpawnItem(
                def.RoomId, def.ItemConfig.ID, pos, Quaternion.identity);

            // Anti-vol : seul le owner de la mission peut pick le colis.
            ServerItemManager.Instance.SetAuthorizedHolder(def.RoomId, _spawnedEntityId, job.OwnerNetId);
            // Éphémère : pas de persistance DB, disparaît au disconnect.
            ServerItemManager.Instance.SetPersistent(def.RoomId, _spawnedEntityId, false);

            job.Context.Set(CtxEntityIdKey, _spawnedEntityId);
            job.Context.Set(CtxRoomIdKey,   def.RoomId);

            GameLogger.System.Info("PickupPackageStep_Spawned {EntityId} {RoomId} {JobId}",
                _spawnedEntityId, def.RoomId, job.Definition.JobId);
        }

        public override void Tick(float dt) {
            var owner = JobTargetRegistry.Instance.GetPlayer(job.OwnerNetId);
            if (owner == null || !owner.IsAvailable) {
                Fail(JobFailureReason.OwnerDisconnected);
                return;
            }

            var entity = ServerItemManager.Instance.GetEntity(def.RoomId, _spawnedEntityId);
            if (entity == null) {
                // Le colis a été despawn par ailleurs (admin, cleanup) — on échoue proprement.
                Fail(JobFailureReason.TargetLost);
                return;
            }

            if (entity.HolderNetId == job.OwnerNetId) {
                Succeed();
            }
        }

        public override void OnExit() {
            // Cleanup uniquement si le step n'a pas succeed (sinon le colis reste
            // dans les mains du joueur pour les steps suivants).
            if (Status == StepStatus.Succeeded) return;
            if (_spawnedEntityId < 0) return;

            ServerItemManager.Instance.DespawnItem(def.RoomId, _spawnedEntityId);
            _spawnedEntityId = -1;
        }
    }
}
