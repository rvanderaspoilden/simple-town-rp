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
        // Réservation de slot active : tant que ces champs sont assignés, le slot
        // est considéré occupé dans la table d'attribution de JobSpawnSlots.
        private JobSpawnSlots _slotsRef;
        private int _reservedSlotIndex = -1;

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

            // Si un JobSpawnSlots est configuré (ex. palette de livraison), on choisit
            // un slot LIBRE via la table d'attribution. Sinon (ou si tous les slots sont
            // déjà réservés), repli sur la position du target + offset.
            Vector3 pos;
            Quaternion rot = Quaternion.identity;
            var slots = JobSpawnSlots.Get(def.SpawnSlotsId);
            int chosenIndex = -1;
            Transform slot = null;
            if (slots != null) {
                if (def.RandomSlot) {
                    var free = slots.GetFreeSlotIndices();
                    if (free.Count > 0) {
                        chosenIndex = free[Random.Range(0, free.Count)];
                    } else {
                        GameLogger.System.Warning("PickupPackageStep_NoFreeSlot {SlotsId} {JobId}",
                            def.SpawnSlotsId, job.Definition.JobId);
                    }
                } else {
                    if (slots.IsSlotFree(def.SpawnSlotIndex)) {
                        chosenIndex = def.SpawnSlotIndex;
                    } else {
                        GameLogger.System.Warning("PickupPackageStep_SlotTaken {SlotsId} {Index} {JobId}",
                            def.SpawnSlotsId, def.SpawnSlotIndex, job.Definition.JobId);
                    }
                }
                slot = chosenIndex >= 0 ? slots.GetSlot(chosenIndex) : null;
            } else if (!string.IsNullOrEmpty(def.SpawnSlotsId)) {
                GameLogger.System.Warning("PickupPackageStep_SlotsNotFound {SlotsId} {JobId}",
                    def.SpawnSlotsId, job.Definition.JobId);
            }

            if (slot != null) {
                pos = slot.position;
                rot = slot.rotation;
            } else {
                pos = target.Transform.position + def.SpawnOffset;
            }

            _spawnedEntityId = ServerItemManager.Instance.SpawnItem(
                def.RoomId, def.ItemConfig.ID, pos, rot);

            // Réservation : marque le slot occupé tant que le step ne libère pas (OnExit).
            if (slots != null && chosenIndex >= 0 && slots.TryReserve(chosenIndex, _spawnedEntityId)) {
                _slotsRef = slots;
                _reservedSlotIndex = chosenIndex;
            }

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
            // Libère la réservation du slot dans TOUS les cas : succès (le joueur a
            // ramassé le colis → le slot est vide) comme échec (le colis va être despawn).
            if (_slotsRef != null && _reservedSlotIndex >= 0) {
                _slotsRef.Release(_reservedSlotIndex);
                _slotsRef = null;
                _reservedSlotIndex = -1;
            }

            // Cleanup despawn uniquement si le step n'a pas succeed (sinon le colis reste
            // dans les mains du joueur pour les steps suivants).
            if (Status == StepStatus.Succeeded) return;
            if (_spawnedEntityId < 0) return;

            ServerItemManager.Instance.DespawnItem(def.RoomId, _spawnedEntityId);
            _spawnedEntityId = -1;
        }
    }
}
