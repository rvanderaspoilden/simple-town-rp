using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Runtime du SortItemsStep. Flow par interaction :
    ///   1. OnEnter spawne tous les items au point de pickup et restreint le
    ///      pickup au joueur propriétaire de la mission.
    ///   2. Le joueur ramasse les items via le système de pickup standard.
    ///   3. Quand il clique USE sur un bac (SortingBin), le client envoie
    ///      MissionSortDepositMessage ; le handler appelle TryDepositFor ici.
    ///   4. On résout l'item tenu contre le bac, on despawn l'item, et on
    ///      push une MissionSortProgressMessage à l'owner pour mettre à jour le HUD.
    ///   5. Quand tous les items sont résolus, on écrit sortAccuracyRatio
    ///      dans le contexte et on Succeed (avec un dernier message Finished=true).
    /// </summary>
    public sealed class SortItemsStepInstance : MissionStepInstance {
        public const string CtxAccuracyKey    = "sortAccuracyRatio";
        public const string CtxCorrectKey     = "sortCorrectCount";
        public const string CtxTotalKey       = "sortTotalCount";

        private sealed class ItemTaskState {
            public int             entityId = -1;
            public int             slotIndex = -1; // index réservé dans MissionSpawnSlots (-1 = pas de slot)
            public bool            resolved;
            public bool            correct;
            public SortingCategory category;
        }

        private static readonly Dictionary<uint, SortItemsStepInstance> _active =
            new Dictionary<uint, SortItemsStepInstance>();

        private readonly SortItemsStepDefinition def;
        private ItemTaskState[] _states;
        private MissionSpawnSlots _slotsRef; // référence pour libération à OnExit

        public SortItemsStepInstance(MissionInstance job, SortItemsStepDefinition definition) : base(job) {
            def = definition;
        }

        public override void OnEnter() {
            var tasks = def.Tasks;
            if (tasks == null || tasks.Count == 0) {
                GameLogger.System.Error("SortItemsStep_NoTasks {MissionId}", job.Definition.MissionId);
                Fail(MissionFailureReason.None);
                return;
            }

            var spawnTarget = job.Context.TargetByKey(def.SpawnAtKey);
            if (spawnTarget == null || !spawnTarget.IsAvailable) {
                Fail(MissionFailureReason.TargetLost);
                return;
            }

            var packageConfig = def.PackageConfig;
            if (packageConfig == null || packageConfig.ID <= 0) {
                GameLogger.System.Error("SortItemsStep_InvalidPackageConfig {MissionId}", job.Definition.MissionId);
                Fail(MissionFailureReason.None);
                return;
            }

            var origin = spawnTarget.Transform.position + def.BaseSpawnOffset;
            _states = new ItemTaskState[tasks.Count];

            var entityIds  = new int[tasks.Count];
            var categories = new SortingCategory[tasks.Count];

            // Slots optionnels : si configurés, chaque colis est posé sur le slot
            // d'index correspondant (position + rotation). Sinon, repli sur
            // l'alignement linéaire au-dessus du target.
            var spawnSlots = MissionSpawnSlots.Get(def.SpawnSlotsId);
            if (!string.IsNullOrEmpty(def.SpawnSlotsId) && spawnSlots == null) {
                GameLogger.System.Warning("SortItemsStep_SlotsNotFound {SlotsId} {MissionId}",
                    def.SpawnSlotsId, job.Definition.MissionId);
            } else if (spawnSlots != null && spawnSlots.SlotCount < tasks.Count) {
                GameLogger.System.Warning("SortItemsStep_NotEnoughSlots {SlotsId} {Slots} {Tasks} {MissionId}",
                    def.SpawnSlotsId, spawnSlots.SlotCount, tasks.Count, job.Definition.MissionId);
            }

            _slotsRef = spawnSlots;

            // Source d'indices :
            //  • Random : pioche dans les slots LIBRES (table d'attribution), mélangés,
            //    pour répartir les colis sur des slots distincts non réservés.
            //  • Séquentiel : indices 0..N-1.
            List<int> slotPool = null;
            if (spawnSlots != null) {
                if (def.RandomSlot) {
                    slotPool = spawnSlots.GetFreeSlotIndices();
                    Shuffle(slotPool);
                } else {
                    slotPool = new List<int>(tasks.Count);
                    for (int i = 0; i < tasks.Count; i++) slotPool.Add(i);
                }
            }

            for (int i = 0; i < tasks.Count; i++) {
                var category = tasks[i].sortingCategory;

                // Pioche le prochain slot CANDIDAT puis tente de le réserver. Si le slot
                // est null / déjà occupé (séquentiel sur slot pris), on retombe sur la
                // disposition linéaire au-dessus du target.
                int slotIndex = -1;
                Transform slot = null;
                if (slotPool != null && i < slotPool.Count) {
                    int candidate = slotPool[i];
                    var cTr = spawnSlots.GetSlot(candidate);
                    if (cTr != null && spawnSlots.IsSlotFree(candidate)) {
                        slotIndex = candidate;
                        slot = cTr;
                    } else if (!def.RandomSlot) {
                        GameLogger.System.Warning("SortItemsStep_SlotTaken {SlotsId} {Index} {MissionId}",
                            def.SpawnSlotsId, candidate, job.Definition.MissionId);
                    }
                }

                Vector3 spawnPos = slot != null ? slot.position : origin + Vector3.right * (i * def.ItemSpacing);
                Quaternion spawnRot = slot != null ? slot.rotation : Quaternion.identity;

                int entityId = ServerItemManager.Instance.SpawnItem(
                    def.RoomId, packageConfig.ID, spawnPos, spawnRot);

                ServerItemManager.Instance.SetAuthorizedHolder(def.RoomId, entityId, job.OwnerNetId);
                ServerItemManager.Instance.SetPersistent(def.RoomId, entityId, false);

                // Réservation du slot dans la table (no-op si slotIndex == -1).
                if (slot != null && spawnSlots.TryReserve(slotIndex, entityId)) {
                    _states[i] = new ItemTaskState { entityId = entityId, slotIndex = slotIndex, category = category };
                } else {
                    _states[i] = new ItemTaskState { entityId = entityId, category = category };
                }
                entityIds[i]  = entityId;
                categories[i] = category;

                GameLogger.System.Info("SortItemsStep_Spawned {EntityId} {Category} {SlotIndex} {MissionId}",
                    entityId, category, _states[i].slotIndex, job.Definition.MissionId);
            }

            // Catégorie = donnée métier : transmise hors du pipeline d'items générique,
            // par message job dédié, à l'owner (après les S2C_SpawnItem → item déjà présent).
            FindOwnerConn()?.Send(new MissionSortItemsSpawnedMessage {
                entityIds  = entityIds,
                categories = categories,
            });

            _active[job.OwnerNetId] = this;
            PushProgress(finished: false);
        }

        public override void Tick(float dt) {
            var owner = MissionTargetRegistry.Instance.GetPlayer(job.OwnerNetId);
            if (owner == null || !owner.IsAvailable) {
                Fail(MissionFailureReason.OwnerDisconnected);
            }
        }

        public override void OnExit() {
            if (_active.TryGetValue(job.OwnerNetId, out var active) && active == this)
                _active.Remove(job.OwnerNetId);

            // Libère toutes les réservations de slot encore actives (à la résolution
            // d'un colis on les a déjà libérés une à une ; on couvre ici le cas
            // échec/abandon où des colis non résolus tiennent encore leurs slots).
            if (_slotsRef != null && _states != null) {
                foreach (var state in _states) {
                    if (state != null && state.slotIndex >= 0) {
                        _slotsRef.Release(state.slotIndex);
                        state.slotIndex = -1;
                    }
                }
            }
            _slotsRef = null;

            if (Status == StepStatus.Succeeded || _states == null) return;
            foreach (var state in _states) {
                if (!state.resolved && state.entityId >= 0)
                    ServerItemManager.Instance.DespawnItem(def.RoomId, state.entityId);
            }
        }

        /// <summary>
        /// Appelé par MissionSystemBootstrap quand le client envoie
        /// MissionSortDepositMessage. Cherche un item de ce step tenu par le joueur,
        /// le résout contre le bac désigné et push la progression.
        /// </summary>
        public static void TryDepositFor(NetworkConnectionToClient conn, string binId) {
            if (conn == null || conn.identity == null) return;
            uint netId = conn.identity.netId;

            if (!_active.TryGetValue(netId, out var step)) {
                conn.Send(new ToastNotificationMessage {
                    text       = "Aucune mission ne te demande de trier des colis.",
                    typeByte   = (byte)NotificationType.JOB,
                    worldToast = true,
                    kindByte   = (byte)ToastKind.Error,
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
                conn.Send(new ToastNotificationMessage {
                    text       = "Tu dois tenir un colis à trier pour utiliser ce bac.",
                    typeByte   = (byte)NotificationType.JOB,
                    worldToast = true,
                    kindByte   = (byte)ToastKind.Error,
                });
                return;
            }

            var bin = SortingBin.Get(binId);
            if (bin == null) {
                GameLogger.System.Warning("SortItemsStep_UnknownBin {BinId} {MissionId}", binId, job.Definition.MissionId);
                return;
            }

            heldState.resolved = true;
            heldState.correct  = bin.AcceptedCategory == heldState.category;

            ServerItemManager.Instance.DespawnItem(def.RoomId, heldState.entityId);
            // Libère le slot d'origine du colis : le step le considère « consommé ».
            if (_slotsRef != null && heldState.slotIndex >= 0) {
                _slotsRef.Release(heldState.slotIndex);
                heldState.slotIndex = -1;
            }

            // Résultat immédiat de l'action du joueur (dépôt dans un bac) → toast flottant,
            // pas une notification coin d'écran. Voir Docs/FEEDBACK_UI.md.
            conn.Send(new ToastNotificationMessage {
                text       = heldState.correct ? "Parfait !" : "Mauvais bac !",
                typeByte   = (byte)NotificationType.JOB,
                worldToast = true,
                kindByte   = (byte)(heldState.correct ? ToastKind.Success : ToastKind.Error),
            });

            if (!heldState.correct) {
                GameLogger.System.Info("SortItemsStep_WrongBin {EntityId} {Category} {BinCategory} {MissionId}",
                    heldState.entityId, heldState.category,
                    bin.AcceptedCategory, job.Definition.MissionId);
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

                GameLogger.System.Info("SortItemsStep_Complete {Correct}/{Total} {Accuracy} {MissionId}",
                    correct, _states.Length, accuracy, job.Definition.MissionId);

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
                rating = (byte)MissionRating.Perfect;
            }

            var conn = FindOwnerConn();
            conn?.Send(new MissionSortProgressMessage {
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

        private static void Shuffle(List<int> list) {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--) {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
