using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Runtime du SortItemsStep. Spawne tous les items dès OnEnter, puis
    /// poll chaque ItemEntity pour détecter pickup (HolderNetId == owner) puis
    /// drop (HolderNetId == 0 après avoir été tenu). À chaque drop : cherche le
    /// bac le plus proche, valide la catégorie, despawn l'item. Écrit
    /// sortAccuracyRatio dans le contexte avant Succeed.
    /// </summary>
    public sealed class SortItemsStepInstance : JobStepInstance {
        public const string CtxAccuracyKey = "sortAccuracyRatio";

        private sealed class ItemTaskState {
            public int      entityId  = -1;
            public bool     wasHeld;
            public bool     resolved;
            public bool     correct;
            public ItemConfig itemConfig;
        }

        private readonly SortItemsStepDefinition def;
        private ItemTaskState[] _states;

        public SortItemsStepInstance(JobInstance job, SortItemsStepDefinition definition) : base(job) {
            def = definition;
        }

        public override void OnEnter() {
            var tasks = def.Tasks;
            if (tasks == null || tasks.Count == 0) {
                GameLogger.System.Error("SortItemsStep_NoTasks {JobId}", job.Definition.JobId);
                Fail(JobFailureReason.None);
                return;
            }

            var spawnTarget = job.Context.TargetByKey(def.SpawnAtKey);
            if (spawnTarget == null || !spawnTarget.IsAvailable) {
                Fail(JobFailureReason.TargetLost);
                return;
            }

            var origin = spawnTarget.Transform.position + def.BaseSpawnOffset;
            _states = new ItemTaskState[tasks.Count];

            for (int i = 0; i < tasks.Count; i++) {
                var cfg = tasks[i].itemConfig;
                if (cfg == null || cfg.ID <= 0) {
                    GameLogger.System.Error("SortItemsStep_InvalidItemConfig {Index} {JobId}", i, job.Definition.JobId);
                    Fail(JobFailureReason.None);
                    return;
                }

                var spawnPos = origin + Vector3.right * (i * def.ItemSpacing);
                int entityId = ServerItemManager.Instance.SpawnItem(
                    def.RoomId, cfg.ID, spawnPos, Quaternion.identity);

                ServerItemManager.Instance.SetAuthorizedHolder(def.RoomId, entityId, job.OwnerNetId);
                ServerItemManager.Instance.SetPersistent(def.RoomId, entityId, false);

                _states[i] = new ItemTaskState { entityId = entityId, itemConfig = cfg };

                GameLogger.System.Info("SortItemsStep_Spawned {EntityId} {Category} {JobId}",
                    entityId, cfg.SortingCategory, job.Definition.JobId);
            }
        }

        public override void Tick(float dt) {
            var owner = JobTargetRegistry.Instance.GetPlayer(job.OwnerNetId);
            if (owner == null || !owner.IsAvailable) {
                Fail(JobFailureReason.OwnerDisconnected);
                return;
            }

            foreach (var state in _states) {
                if (state.resolved) continue;

                var entity = ServerItemManager.Instance.GetEntity(def.RoomId, state.entityId);

                if (entity == null) {
                    state.resolved = true;
                    state.correct  = false;
                    continue;
                }

                if (entity.HolderNetId == job.OwnerNetId) {
                    state.wasHeld = true;
                    continue;
                }

                if (state.wasHeld && entity.HolderNetId == 0) {
                    var bin = SortingBin.FindClosest(entity.Position, def.BinDropRadius);
                    state.correct  = bin != null && bin.AcceptedCategory == state.itemConfig.SortingCategory;
                    state.resolved = true;

                    ServerItemManager.Instance.DespawnItem(def.RoomId, state.entityId);

                    if (!state.correct) {
                        var conn = FindOwnerConn();
                        conn?.Send(new JobNotificationMessage { text = "Mauvais bac !" });
                        GameLogger.System.Info("SortItemsStep_WrongBin {EntityId} {Category} {BinCategory} {JobId}",
                            state.entityId,
                            state.itemConfig.SortingCategory,
                            bin != null ? bin.AcceptedCategory.ToString() : "none",
                            job.Definition.JobId);
                    }
                }
            }

            // Vérification après la boucle : un item peut être résolu dans ce tick même.
            bool allResolved = true;
            foreach (var s in _states) {
                if (!s.resolved) { allResolved = false; break; }
            }

            if (allResolved) {
                int correct = 0;
                foreach (var s in _states) if (s.correct) correct++;
                float accuracy = _states.Length > 0 ? (float)correct / _states.Length : 1f;
                job.Context.Set(CtxAccuracyKey, accuracy);

                GameLogger.System.Info("SortItemsStep_Complete {Correct}/{Total} {Accuracy} {JobId}",
                    correct, _states.Length, accuracy, job.Definition.JobId);

                Succeed();
            }
        }

        public override void OnExit() {
            if (Status == StepStatus.Succeeded || _states == null) return;
            foreach (var state in _states) {
                if (!state.resolved && state.entityId >= 0)
                    ServerItemManager.Instance.DespawnItem(def.RoomId, state.entityId);
            }
        }

        private NetworkConnectionToClient FindOwnerConn() {
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return null;
            return identity != null ? identity.connectionToClient : null;
        }
    }
}
