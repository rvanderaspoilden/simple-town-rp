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

            // Si un JobSpawnSlots est configuré (ex. palette de livraison), on pose le
            // colis sur le slot demandé (position + rotation). Sinon, ancien comportement :
            // position du target + offset.
            Vector3 pos;
            Quaternion rot = Quaternion.identity;
            var slots = JobSpawnSlots.Get(def.SpawnSlotsId);
            Transform slot = null;
            if (slots != null) {
                if (def.RandomSlot) {
                    // Tirage d'un slot LIBRE au hasard (aucun item posé dans le rayon).
                    slot = slots.GetRandomSlot(
                        t => !ServerItemManager.Instance.IsWorldPositionOccupied(def.RoomId, t.position, def.SlotOccupancyRadius),
                        out _, out bool wasFree);
                    if (slot != null && !wasFree) {
                        GameLogger.System.Warning("PickupPackageStep_NoFreeSlot {SlotsId} {JobId}",
                            def.SpawnSlotsId, job.Definition.JobId);
                    }
                } else {
                    slot = slots.GetSlot(def.SpawnSlotIndex);
                }
            }
            if (slot != null) {
                pos = slot.position;
                rot = slot.rotation;
            } else {
                if (!string.IsNullOrEmpty(def.SpawnSlotsId)) {
                    GameLogger.System.Warning("PickupPackageStep_SlotsNotFound {SlotsId} {Index} {JobId}",
                        def.SpawnSlotsId, def.SpawnSlotIndex, job.Definition.JobId);
                }
                pos = target.Transform.position + def.SpawnOffset;
            }

            _spawnedEntityId = ServerItemManager.Instance.SpawnItem(
                def.RoomId, def.ItemConfig.ID, pos, rot);

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
