using System.Collections.Generic;
using Mirror;
using Sim.Logging;

namespace Sim.Missions {
    /// <summary>
    /// Manager autoritaire des missions côté serveur. Plain C# singleton, même
    /// pattern que NpcServerManager. Pilote :
    ///   - Offer/Accept/Abandon
    ///   - Tick global (appelé par MissionServerTicker)
    ///   - Broadcast des transitions vers le owner via NetworkMessage
    ///
    /// La logique métier (steps, rewards) vit dans MissionInstance et les modules
    /// associés. Ce manager fait uniquement l'orchestration + réseau.
    /// </summary>
    public class MissionServerManager {
        private static MissionServerManager _instance;
        public static MissionServerManager Instance => _instance ??= new MissionServerManager();

        private readonly Dictionary<string, MissionInstance> _byInstanceId = new Dictionary<string, MissionInstance>();
        private readonly Dictionary<uint, List<MissionInstance>> _byOwner = new Dictionary<uint, List<MissionInstance>>();
        private readonly List<string> _toRemove = new List<string>();

        private bool _subscribed;

        public IEnumerable<MissionInstance> Active => _byInstanceId.Values;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Subscribe() {
            if (_subscribed) return;
            MissionEvents.StepAdvanced += OnStepAdvanced;
            MissionEvents.MissionCompleted += OnMissionFinished;
            MissionEvents.MissionFailed    += OnMissionFinished;
            _subscribed = true;
        }

        public void Unsubscribe() {
            if (!_subscribed) return;
            MissionEvents.StepAdvanced -= OnStepAdvanced;
            MissionEvents.MissionCompleted -= OnMissionFinished;
            MissionEvents.MissionFailed    -= OnMissionFinished;
            _subscribed = false;
        }

        public void Reset() {
            Unsubscribe();
            _byInstanceId.Clear();
            _byOwner.Clear();
            _toRemove.Clear();
            GameLogger.System.Info("MissionServerManagerReset");
        }

        // ── Public API ────────────────────────────────────────────────────────

        public MissionInstance Offer(MissionDefinition def, uint ownerNetId, MissionContext context) {
            if (def == null) return null;

            if (IsGlobalCapReached(def)) {
                GameLogger.System.Debug("JobOfferDenied_MaxGlobal {MissionId} {Cap}",
                    def.MissionId, def.MaxConcurrentGlobal);
                return null;
            }

            int active = ActiveCountForOwner(ownerNetId);
            if (active >= def.MaxConcurrentPerPlayer) {
                GameLogger.System.Debug("JobOfferDenied_MaxConcurrent {MissionId} {NetId} {Active}",
                    def.MissionId, ownerNetId, active);
                return null;
            }

            var job = MissionInstance.CreateOffer(def, ownerNetId, context);
            _byInstanceId[job.InstanceId] = job;
            AddToOwnerIndex(ownerNetId, job);

            SendOffered(job);
            MissionEvents.RaiseMissionOffered(job);
            return job;
        }

        public MissionInstance Publish(MissionDefinition def, MissionContext context) {
            if (def == null) return null;

            if (IsGlobalCapReached(def)) {
                GameLogger.System.Debug("JobPublishDenied_MaxGlobal {MissionId} {Cap}",
                    def.MissionId, def.MaxConcurrentGlobal);
                return null;
            }

            var job = MissionInstance.CreatePublished(def, context);
            _byInstanceId[job.InstanceId] = job;
            MissionEvents.RaiseMissionPublished(job);

            // Only notify players whose active career matches this job's
            // category AND who opted in to new-mission notifications (Settings
            // app → notificationsNewMission). Matches the board gating model.
            var label = MissionLabel(def);
            var notif = new MissionNotificationMessage { text = $"Nouvelle mission : {label}" };
            foreach (var conn in NetworkServer.connections.Values) {
                if (conn?.identity == null) continue;
                var player = conn.identity.GetComponent<Sim.PlayerController>();
                if (player == null || player.CharacterData == null) continue;
                if (player.CharacterData.CurrentProfessionId != def.ProfessionId) continue;
                if (player.UserSettings != null && !player.UserSettings.NotificationsNewMission) continue;
                conn.Send(notif);
            }
            return job;
        }

        public bool TakeFromBoard(string instanceId, NetworkConnectionToClient sender) {
            if (!_byInstanceId.TryGetValue(instanceId, out var job)) return false;
            if (sender == null || sender.identity == null) return false;
            if (!job.IsAvailable) return false;

            uint netId = sender.identity.netId;

            // Career gate (defense in depth, mirror of MissionBoardServer.OpenBoard).
            var player = sender.identity.GetComponent<Sim.PlayerController>();
            var playerJob = player != null && player.CharacterData != null
                ? player.CharacterData.CurrentProfessionId
                : "";
            if (playerJob != job.Definition.ProfessionId) {
                GameLogger.System.Debug("JobTakeDenied_WrongProfession {MissionId} {NetId} {PlayerJob} {Required}",
                    job.Definition.MissionId, netId, playerJob, job.Definition.ProfessionId);
                sender.Send(new MissionNotificationMessage {
                    text = "Cette mission n'est pas pour ton métier."
                });
                return false;
            }

            // Constellation gate: a mission may require a specific node to be
            // unlocked (e.g. the delivery node gates delivery missions). The server
            // checks the player's unlocked-node cache (hydrated at connect, refreshed
            // on unlock via PlayerConstellation.CmdNotifyNodeUnlocked).
            var requiredNodeId = job.Definition.RequiredNodeId;
            if (!string.IsNullOrEmpty(requiredNodeId)) {
                var constellation = player != null ? player.GetComponent<Sim.Player.PlayerConstellation>() : null;
                if (constellation == null || !constellation.ServerHasUnlockedNode(requiredNodeId)) {
                    GameLogger.System.Debug("JobTakeDenied_NodeLocked {MissionId} {NetId} {RequiredNode}",
                        job.Definition.MissionId, netId, requiredNodeId);
                    sender.Send(new MissionNotificationMessage {
                        text = "Tu dois débloquer la compétence requise dans ta constellation."
                    });
                    return false;
                }
            }

            int active = ActiveCountForOwner(netId);
            if (active >= job.Definition.MaxConcurrentPerPlayer) {
                GameLogger.System.Debug("JobTakeDenied_MaxConcurrent {MissionId} {NetId} {Active}",
                    job.Definition.MissionId, netId, active);
                sender.Send(new MissionNotificationMessage {
                    text = "Tu as déjà une mission en cours."
                });
                return false;
            }

            if (!job.Take(netId)) return false;
            AddToOwnerIndex(netId, job);
            SendOffered(job);
            SendToOwner(netId, new MissionNotificationMessage {
                text = $"Mission prise : {MissionLabel(job.Definition)}"
            });
            return true;
        }

        private void AddToOwnerIndex(uint netId, MissionInstance job) {
            if (netId == 0u) return;
            if (!_byOwner.TryGetValue(netId, out var list)) {
                list = new List<MissionInstance>();
                _byOwner[netId] = list;
            }
            list.Add(job);
        }

        public bool Accept(string instanceId, NetworkConnectionToClient sender) {
            if (!_byInstanceId.TryGetValue(instanceId, out var job)) return false;
            if (sender == null || sender.identity == null) return false;
            if (sender.identity.netId != job.OwnerNetId) return false;
            if (job.Status != MissionStatus.Offered) return false;

            job.Accept();
            SendOffered(job);
            SendToOwner(job.OwnerNetId, new MissionNotificationMessage {
                text = $"Mission prise : {MissionLabel(job.Definition)}"
            });
            return true;
        }

        public bool Abandon(string instanceId, NetworkConnectionToClient sender) {
            if (!_byInstanceId.TryGetValue(instanceId, out var job)) return false;
            if (sender == null || sender.identity == null) return false;
            if (sender.identity.netId != job.OwnerNetId) return false;

            var label = MissionLabel(job.Definition);
            job.Abandon();
            SendToOwner(job.OwnerNetId, new MissionNotificationMessage {
                text = $"Mission annulée : {label}"
            });
            return true;
        }

        private static string MissionLabel(MissionDefinition def)
            => string.IsNullOrEmpty(def.DisplayNameKey) ? def.MissionId : def.DisplayNameKey;

        /// <summary>
        /// Termine toutes les missions actives/offertes d'un joueur lorsqu'il s'évanouit
        /// (mort). Chaque job échoue → MissionItemCleanup despawn l'éventuel item de mission
        /// porté, et OnMissionFinished notifie le client owner pour nettoyer son UI de mission.
        /// </summary>
        public void AbandonAllForOwner(uint netId) {
            if (netId == 0u) return;
            if (!_byOwner.TryGetValue(netId, out var list)) return;
            // Copie : Abandon() lève des events qui peuvent muter _byOwner.
            foreach (var job in new List<MissionInstance>(list)) {
                if (job.Status == MissionStatus.Offered || job.Status == MissionStatus.Active) {
                    job.Abandon();
                }
            }
        }

        // Appelé par SimpleTownNetwork quand un joueur se déconnecte (à wirer).
        public void OnPlayerDisconnected(uint netId) {
            if (!_byOwner.TryGetValue(netId, out var list)) return;
            foreach (var job in list) {
                if (job.Status == MissionStatus.Offered || job.Status == MissionStatus.Active) {
                    job.OwnerDisconnected();
                }
            }
        }

        public void Tick(float dt) {
            if (!NetworkServer.active) return;

            foreach (var job in _byInstanceId.Values) {
                if (job.Status == MissionStatus.Active || job.Status == MissionStatus.Available) job.Tick(dt);
            }

            if (_toRemove.Count > 0) {
                foreach (var id in _toRemove) {
                    if (_byInstanceId.TryGetValue(id, out var job)) {
                        if (_byOwner.TryGetValue(job.OwnerNetId, out var list)) {
                            list.Remove(job);
                            if (list.Count == 0) _byOwner.Remove(job.OwnerNetId);
                        }
                        _byInstanceId.Remove(id);
                    }
                }
                _toRemove.Clear();
            }
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private int ActiveCountForOwner(uint netId) {
            if (!_byOwner.TryGetValue(netId, out var list)) return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++) {
                var s = list[i].Status;
                if (s == MissionStatus.Offered || s == MissionStatus.Active) n++;
            }
            return n;
        }

        /// <summary>
        /// Cap GLOBAL (monde, tous joueurs) par définition : vrai si le nombre
        /// d'instances vivantes (Available/Offered/Active) de cette mission atteint
        /// déjà <see cref="MissionDefinition.MaxConcurrentGlobal"/>. 0 = illimité. Sert à
        /// n'avoir qu'une mission par spot physique (1 machine d'emballage, 1 étagère
        /// de tri…). Les états terminaux ne comptent pas → le compteur se libère dès
        /// qu'une mission se termine, sans dépendre du nettoyage différé.
        /// </summary>
        private bool IsGlobalCapReached(MissionDefinition def) {
            int cap = def.MaxConcurrentGlobal;
            if (cap <= 0) return false; // illimité
            int n = 0;
            foreach (var job in _byInstanceId.Values) {
                if (job.Definition == null || job.Definition.MissionId != def.MissionId) continue;
                var s = job.Status;
                if (s == MissionStatus.Available || s == MissionStatus.Offered || s == MissionStatus.Active) {
                    if (++n >= cap) return true;
                }
            }
            return false;
        }

        private void OnStepAdvanced(MissionInstance job) {
            var stepDef = job.CurrentStepIndex < job.Definition.Steps.Count
                ? job.Definition.Steps[job.CurrentStepIndex]
                : null;

            var activeTarget = ResolveActiveTarget(job, stepDef);
            var msg = new MissionStepAdvancedMessage {
                instanceId = job.InstanceId,
                newStepIndex = job.CurrentStepIndex,
                promptKey = stepDef != null ? stepDef.PromptKey : string.Empty,
                currentTargetId = activeTarget?.TargetId ?? string.Empty,
                currentTargetName = activeTarget?.DisplayName ?? string.Empty,
                showTargetBeacon = stepDef != null && stepDef.ShowsTargetBeacon
            };
            SendToOwner(job.OwnerNetId, msg);
        }

        private static IMissionTarget ResolveActiveTarget(MissionInstance job, MissionStepDefinition stepDef) {
            if (stepDef == null) return null;
            return job.Context.TargetByKey(stepDef.GetActiveTargetKey());
        }

        private void OnMissionFinished(MissionInstance job) {
            byte rating = 0;
            var scoring = job.Definition?.ScoringDefinition;
            if (job.Status == MissionStatus.Completed) {
                rating = (byte)(scoring != null ? scoring.Evaluate(job) : MissionRating.Perfect);
            }
            job.Context.TryGetStruct<int>(SortItemsStepInstance.CtxCorrectKey, out int sortCorrect);
            job.Context.TryGetStruct<int>(SortItemsStepInstance.CtxTotalKey,   out int sortTotal);
            // Flatten constellation accumulators into parallel arrays.
            // Branches first, then professions — order is irrelevant for the toast
            // since labels are embedded, but keeps a stable presentation.
            int gainCount = job.BranchPointsEarned.Count + job.ProfessionPointsEarned.Count;
            string[] gainLabels  = gainCount > 0 ? new string[gainCount] : null;
            int[]    gainAmounts = gainCount > 0 ? new int[gainCount]    : null;
            if (gainCount > 0) {
                int gi = 0;
                foreach (var kv in job.BranchPointsEarned) {
                    job.ConstellationGainsLabels.TryGetValue(kv.Key, out var lbl);
                    gainLabels[gi] = string.IsNullOrEmpty(lbl) ? kv.Key : lbl;
                    gainAmounts[gi] = kv.Value;
                    gi++;
                }
                foreach (var kv in job.ProfessionPointsEarned) {
                    job.ConstellationGainsLabels.TryGetValue(kv.Key, out var lbl);
                    gainLabels[gi] = string.IsNullOrEmpty(lbl) ? kv.Key : lbl;
                    gainAmounts[gi] = kv.Value;
                    gi++;
                }
            }

            var msg = new MissionFinishedMessage {
                instanceId     = job.InstanceId,
                terminalStatus = job.Status,
                failureReason  = job.FailureReason,
                rating         = rating,
                elapsedSeconds = job.ElapsedSeconds,
                correctCount   = sortCorrect,
                totalCount     = sortTotal,
                moneyEarned    = job.MoneyEarned,
                constellationGainLabels  = gainLabels,
                constellationGainAmounts = gainAmounts,
            };
            SendToOwner(job.OwnerNetId, msg);
            _toRemove.Add(job.InstanceId);
        }

        private void SendOffered(MissionInstance job) {
            var ctx = job.Context;
            var stepDef = job.CurrentStepIndex < job.Definition.Steps.Count
                ? job.Definition.Steps[job.CurrentStepIndex]
                : null;

            var activeTarget = ResolveActiveTarget(job, stepDef);
            var msg = new MissionOfferedMessage {
                instanceId = job.InstanceId,
                missionId = job.Definition.MissionId,
                statusByte = (byte)job.Status,
                currentStepIndex = job.CurrentStepIndex,
                currentPromptKey = stepDef != null ? stepDef.PromptKey : string.Empty,
                currentTargetId = activeTarget?.TargetId ?? string.Empty,
                currentTargetName = activeTarget?.DisplayName ?? string.Empty,
                showTargetBeacon = stepDef != null && stepDef.ShowsTargetBeacon,
                primaryTargetKind = ctx.primaryTarget?.Kind ?? MissionTargetKind.Zone,
                primaryTargetId   = ctx.primaryTarget?.TargetId ?? string.Empty,
                primaryTargetName = ctx.primaryTarget?.DisplayName ?? string.Empty,
                secondaryTargetKind = ctx.secondaryTarget?.Kind ?? MissionTargetKind.Zone,
                secondaryTargetId   = ctx.secondaryTarget?.TargetId ?? string.Empty,
                secondaryTargetName = ctx.secondaryTarget?.DisplayName ?? string.Empty,
                payloadItemId = ctx.payloadItemId ?? string.Empty,
                elapsedSeconds = job.ElapsedSeconds,
            };
            SendToOwner(job.OwnerNetId, msg);
        }

        private static void SendToOwner<T>(uint ownerNetId, T msg) where T : struct, NetworkMessage {
            if (!NetworkServer.spawned.TryGetValue(ownerNetId, out var identity)) return;
            var conn = identity.connectionToClient;
            if (conn == null) return;
            conn.Send(msg);
        }
    }
}
