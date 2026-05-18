using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Runtime du SortItemsStep. Flow par interaction :
    ///   1. OnEnter spawne tous les items au point de pickup et restreint le
    ///      pickup au joueur propriétaire de la mission.
    ///   2. Le joueur ramasse les items via le système de pickup standard.
    ///   3. Quand il clique USE sur un bac (SortingBin), le client envoie
    ///      JobSortDepositMessage ; le handler appelle TryDepositFor ici.
    ///   4. On résout l'item tenu contre le bac, on despawn l'item, et on
    ///      push une JobSortProgressMessage à l'owner pour mettre à jour le HUD.
    ///   5. Quand tous les items sont résolus, on écrit sortAccuracyRatio
    ///      dans le contexte et on Succeed (avec un dernier message Finished=true).
    /// </summary>
    public sealed class SortItemsStepInstance : JobStepInstance {
        public const string CtxAccuracyKey    = "sortAccuracyRatio";
        public const string CtxCorrectKey     = "sortCorrectCount";
        public const string CtxTotalKey       = "sortTotalCount";

        private sealed class ItemTaskState {
            public int        entityId = -1;
            public bool       resolved;
            public bool       correct;
            public ItemConfig itemConfig;
        }

        private static readonly Dictionary<uint, SortItemsStepInstance> _active =
            new Dictionary<uint, SortItemsStepInstance>();

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

            _active[job.OwnerNetId] = this;
            PushProgress(finished: false);
        }

        public override void Tick(float dt) {
            var owner = JobTargetRegistry.Instance.GetPlayer(job.OwnerNetId);
            if (owner == null || !owner.IsAvailable) {
                Fail(JobFailureReason.OwnerDisconnected);
            }
        }

        public override void OnExit() {
            if (_active.TryGetValue(job.OwnerNetId, out var active) && active == this)
                _active.Remove(job.OwnerNetId);

            if (Status == StepStatus.Succeeded || _states == null) return;
            foreach (var state in _states) {
                if (!state.resolved && state.entityId >= 0)
                    ServerItemManager.Instance.DespawnItem(def.RoomId, state.entityId);
            }
        }

        /// <summary>
        /// Appelé par JobSystemBootstrap quand le client envoie
        /// JobSortDepositMessage. Cherche un item de ce step tenu par le joueur,
        /// le résout contre le bac désigné et push la progression.
        /// </summary>
        public static void TryDepositFor(NetworkConnectionToClient conn, string binId) {
            if (conn == null || conn.identity == null) return;
            uint netId = conn.identity.netId;

            if (!_active.TryGetValue(netId, out var step)) {
                conn.Send(new JobNotificationMessage {
                    text = "Aucune mission ne te demande de trier des colis."
                });
                return;
            }
            step.HandleDeposit(conn, binId);
        }

        private void HandleDeposit(NetworkConnectionToClient conn, string binId) {
            ItemTaskState heldState = null;
            foreach (var state in _states) {
                if (state.resolved) continue;
                var entity = ServerItemManager.Instance.GetEntity(def.RoomId, state.entityId);
                if (entity == null) {
                    // Item disparu (cleanup, drop hors radar) — on le considère résolu/raté.
                    state.resolved = true;
                    state.correct  = false;
                    continue;
                }
                if (entity.HolderNetId == job.OwnerNetId) {
                    heldState = state;
                    break;
                }
            }

            if (heldState == null) {
                conn.Send(new JobNotificationMessage {
                    text = "Tu dois tenir un colis à trier pour utiliser ce bac."
                });
                return;
            }

            var bin = SortingBin.Get(binId);
            if (bin == null) {
                GameLogger.System.Warning("SortItemsStep_UnknownBin {BinId} {JobId}", binId, job.Definition.JobId);
                return;
            }

            heldState.resolved = true;
            heldState.correct  = bin.AcceptedCategory == heldState.itemConfig.SortingCategory;

            ServerItemManager.Instance.DespawnItem(def.RoomId, heldState.entityId);

            if (!heldState.correct) {
                conn.Send(new JobNotificationMessage { text = "Mauvais bac !" });
                GameLogger.System.Info("SortItemsStep_WrongBin {EntityId} {Category} {BinCategory} {JobId}",
                    heldState.entityId, heldState.itemConfig.SortingCategory,
                    bin.AcceptedCategory, job.Definition.JobId);
            }

            bool allResolved = true;
            foreach (var s in _states) {
                if (!s.resolved) { allResolved = false; break; }
            }

            if (allResolved) {
                int correct = 0;
                foreach (var s in _states) if (s.correct) correct++;
                float accuracy = _states.Length > 0 ? (float)correct / _states.Length : 1f;
                job.Context.Set(CtxAccuracyKey, accuracy);
                job.Context.Set(CtxCorrectKey, correct);
                job.Context.Set(CtxTotalKey,   _states.Length);

                GameLogger.System.Info("SortItemsStep_Complete {Correct}/{Total} {Accuracy} {JobId}",
                    correct, _states.Length, accuracy, job.Definition.JobId);

                PushProgress(finished: true);
                Succeed();
            } else {
                PushProgress(finished: false);
            }
        }

        private void PushProgress(bool finished) {
            if (_states == null) return;
            int resolved = 0, correct = 0;
            foreach (var s in _states) {
                if (s.resolved) {
                    resolved++;
                    if (s.correct) correct++;
                }
            }
            float ratio = _states.Length > 0 ? (float)correct / _states.Length : 1f;
            byte rating = 0;
            if (finished && job.Definition.ScoringDefinition != null) {
                rating = (byte)job.Definition.ScoringDefinition.Evaluate(job);
            } else if (finished) {
                rating = (byte)JobRating.Perfect;
            }

            var conn = FindOwnerConn();
            conn?.Send(new JobSortProgressMessage {
                instanceId    = job.InstanceId,
                resolvedCount = resolved,
                correctCount  = correct,
                totalCount    = _states.Length,
                finished      = finished,
                accuracyRatio = ratio,
                rating        = rating,
            });
        }

        private NetworkConnectionToClient FindOwnerConn() {
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return null;
            return identity != null ? identity.connectionToClient : null;
        }
    }
}
