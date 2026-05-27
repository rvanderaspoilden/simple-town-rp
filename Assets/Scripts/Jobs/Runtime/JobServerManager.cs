using System.Collections.Generic;
using Mirror;
using Sim.Logging;

namespace Sim.Jobs {
    /// <summary>
    /// Manager autoritaire des missions côté serveur. Plain C# singleton, même
    /// pattern que NpcServerManager. Pilote :
    ///   - Offer/Accept/Abandon
    ///   - Tick global (appelé par JobServerTicker)
    ///   - Broadcast des transitions vers le owner via NetworkMessage
    ///
    /// La logique métier (steps, rewards) vit dans JobInstance et les modules
    /// associés. Ce manager fait uniquement l'orchestration + réseau.
    /// </summary>
    public class JobServerManager {
        private static JobServerManager _instance;
        public static JobServerManager Instance => _instance ??= new JobServerManager();

        private readonly Dictionary<string, JobInstance> _byInstanceId = new Dictionary<string, JobInstance>();
        private readonly Dictionary<uint, List<JobInstance>> _byOwner = new Dictionary<uint, List<JobInstance>>();
        private readonly List<string> _toRemove = new List<string>();

        private bool _subscribed;

        public IEnumerable<JobInstance> Active => _byInstanceId.Values;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Subscribe() {
            if (_subscribed) return;
            JobEvents.StepAdvanced += OnStepAdvanced;
            JobEvents.JobCompleted += OnJobFinished;
            JobEvents.JobFailed    += OnJobFinished;
            _subscribed = true;
        }

        public void Unsubscribe() {
            if (!_subscribed) return;
            JobEvents.StepAdvanced -= OnStepAdvanced;
            JobEvents.JobCompleted -= OnJobFinished;
            JobEvents.JobFailed    -= OnJobFinished;
            _subscribed = false;
        }

        public void Reset() {
            Unsubscribe();
            _byInstanceId.Clear();
            _byOwner.Clear();
            _toRemove.Clear();
            GameLogger.System.Info("JobServerManagerReset");
        }

        // ── Public API ────────────────────────────────────────────────────────

        public JobInstance Offer(JobDefinition def, uint ownerNetId, JobContext context) {
            if (def == null) return null;

            if (IsGlobalCapReached(def)) {
                GameLogger.System.Debug("JobOfferDenied_MaxGlobal {JobId} {Cap}",
                    def.JobId, def.MaxConcurrentGlobal);
                return null;
            }

            int active = ActiveCountForOwner(ownerNetId);
            if (active >= def.MaxConcurrentPerPlayer) {
                GameLogger.System.Debug("JobOfferDenied_MaxConcurrent {JobId} {NetId} {Active}",
                    def.JobId, ownerNetId, active);
                return null;
            }

            var job = JobInstance.CreateOffer(def, ownerNetId, context);
            _byInstanceId[job.InstanceId] = job;
            AddToOwnerIndex(ownerNetId, job);

            SendOffered(job);
            JobEvents.RaiseJobOffered(job);
            return job;
        }

        public JobInstance Publish(JobDefinition def, JobContext context) {
            if (def == null) return null;

            if (IsGlobalCapReached(def)) {
                GameLogger.System.Debug("JobPublishDenied_MaxGlobal {JobId} {Cap}",
                    def.JobId, def.MaxConcurrentGlobal);
                return null;
            }

            var job = JobInstance.CreatePublished(def, context);
            _byInstanceId[job.InstanceId] = job;
            JobEvents.RaiseJobPublished(job);

            // Only notify players whose active career matches this job's
            // category AND who opted in to new-mission notifications (Settings
            // app → notificationsNewMission). Matches the board gating model.
            var label = JobLabel(def);
            var notif = new JobNotificationMessage { text = $"Nouvelle mission : {label}" };
            foreach (var conn in NetworkServer.connections.Values) {
                if (conn?.identity == null) continue;
                var player = conn.identity.GetComponent<Sim.PlayerController>();
                if (player == null || player.CharacterData == null) continue;
                if (player.CharacterData.CurrentJobCategory != def.Category) continue;
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

            // Career gate (defense in depth, mirror of JobBoardServer.OpenBoard).
            var player = sender.identity.GetComponent<Sim.PlayerController>();
            var playerJob = player != null && player.CharacterData != null
                ? player.CharacterData.CurrentJobCategory
                : null;
            if (playerJob != job.Definition.Category) {
                GameLogger.System.Debug("JobTakeDenied_WrongCategory {JobId} {NetId} {PlayerJob} {Required}",
                    job.Definition.JobId, netId, playerJob, job.Definition.Category);
                sender.Send(new JobNotificationMessage {
                    text = "Cette mission n'est pas pour ton métier."
                });
                return false;
            }

            int active = ActiveCountForOwner(netId);
            if (active >= job.Definition.MaxConcurrentPerPlayer) {
                GameLogger.System.Debug("JobTakeDenied_MaxConcurrent {JobId} {NetId} {Active}",
                    job.Definition.JobId, netId, active);
                sender.Send(new JobNotificationMessage {
                    text = "Tu as déjà une mission en cours."
                });
                return false;
            }

            if (!job.Take(netId)) return false;
            AddToOwnerIndex(netId, job);
            SendOffered(job);
            SendToOwner(netId, new JobNotificationMessage {
                text = $"Mission prise : {JobLabel(job.Definition)}"
            });
            return true;
        }

        private void AddToOwnerIndex(uint netId, JobInstance job) {
            if (netId == 0u) return;
            if (!_byOwner.TryGetValue(netId, out var list)) {
                list = new List<JobInstance>();
                _byOwner[netId] = list;
            }
            list.Add(job);
        }

        public bool Accept(string instanceId, NetworkConnectionToClient sender) {
            if (!_byInstanceId.TryGetValue(instanceId, out var job)) return false;
            if (sender == null || sender.identity == null) return false;
            if (sender.identity.netId != job.OwnerNetId) return false;
            if (job.Status != JobStatus.Offered) return false;

            job.Accept();
            SendOffered(job);
            SendToOwner(job.OwnerNetId, new JobNotificationMessage {
                text = $"Mission prise : {JobLabel(job.Definition)}"
            });
            return true;
        }

        public bool Abandon(string instanceId, NetworkConnectionToClient sender) {
            if (!_byInstanceId.TryGetValue(instanceId, out var job)) return false;
            if (sender == null || sender.identity == null) return false;
            if (sender.identity.netId != job.OwnerNetId) return false;

            var label = JobLabel(job.Definition);
            job.Abandon();
            SendToOwner(job.OwnerNetId, new JobNotificationMessage {
                text = $"Mission annulée : {label}"
            });
            return true;
        }

        private static string JobLabel(JobDefinition def)
            => string.IsNullOrEmpty(def.DisplayNameKey) ? def.JobId : def.DisplayNameKey;

        /// <summary>
        /// Termine toutes les missions actives/offertes d'un joueur lorsqu'il s'évanouit
        /// (mort). Chaque job échoue → JobItemCleanup despawn l'éventuel item de mission
        /// porté, et OnJobFinished notifie le client owner pour nettoyer son UI de mission.
        /// </summary>
        public void AbandonAllForOwner(uint netId) {
            if (netId == 0u) return;
            if (!_byOwner.TryGetValue(netId, out var list)) return;
            // Copie : Abandon() lève des events qui peuvent muter _byOwner.
            foreach (var job in new List<JobInstance>(list)) {
                if (job.Status == JobStatus.Offered || job.Status == JobStatus.Active) {
                    job.Abandon();
                }
            }
        }

        // Appelé par SimpleTownNetwork quand un joueur se déconnecte (à wirer).
        public void OnPlayerDisconnected(uint netId) {
            if (!_byOwner.TryGetValue(netId, out var list)) return;
            foreach (var job in list) {
                if (job.Status == JobStatus.Offered || job.Status == JobStatus.Active) {
                    job.OwnerDisconnected();
                }
            }
        }

        public void Tick(float dt) {
            if (!NetworkServer.active) return;

            foreach (var job in _byInstanceId.Values) {
                if (job.Status == JobStatus.Active || job.Status == JobStatus.Available) job.Tick(dt);
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
                if (s == JobStatus.Offered || s == JobStatus.Active) n++;
            }
            return n;
        }

        /// <summary>
        /// Cap GLOBAL (monde, tous joueurs) par définition : vrai si le nombre
        /// d'instances vivantes (Available/Offered/Active) de cette mission atteint
        /// déjà <see cref="JobDefinition.MaxConcurrentGlobal"/>. 0 = illimité. Sert à
        /// n'avoir qu'une mission par spot physique (1 machine d'emballage, 1 étagère
        /// de tri…). Les états terminaux ne comptent pas → le compteur se libère dès
        /// qu'une mission se termine, sans dépendre du nettoyage différé.
        /// </summary>
        private bool IsGlobalCapReached(JobDefinition def) {
            int cap = def.MaxConcurrentGlobal;
            if (cap <= 0) return false; // illimité
            int n = 0;
            foreach (var job in _byInstanceId.Values) {
                if (job.Definition == null || job.Definition.JobId != def.JobId) continue;
                var s = job.Status;
                if (s == JobStatus.Available || s == JobStatus.Offered || s == JobStatus.Active) {
                    if (++n >= cap) return true;
                }
            }
            return false;
        }

        private void OnStepAdvanced(JobInstance job) {
            var stepDef = job.CurrentStepIndex < job.Definition.Steps.Count
                ? job.Definition.Steps[job.CurrentStepIndex]
                : null;

            var activeTarget = ResolveActiveTarget(job, stepDef);
            var msg = new JobStepAdvancedMessage {
                instanceId = job.InstanceId,
                newStepIndex = job.CurrentStepIndex,
                promptKey = stepDef != null ? stepDef.PromptKey : string.Empty,
                currentTargetId = activeTarget?.TargetId ?? string.Empty,
                currentTargetName = activeTarget?.DisplayName ?? string.Empty,
                showTargetBeacon = stepDef != null && stepDef.ShowsTargetBeacon
            };
            SendToOwner(job.OwnerNetId, msg);
        }

        private static IJobTarget ResolveActiveTarget(JobInstance job, JobStepDefinition stepDef) {
            if (stepDef == null) return null;
            return job.Context.TargetByKey(stepDef.GetActiveTargetKey());
        }

        private void OnJobFinished(JobInstance job) {
            byte rating = 0;
            byte variant = (byte)JobResultVariant.Default;
            var scoring = job.Definition?.ScoringDefinition;
            if (job.Status == JobStatus.Completed) {
                rating = (byte)(scoring != null ? scoring.Evaluate(job) : JobRating.Perfect);
            }
            if (scoring != null) variant = (byte)scoring.ResultVariant;
            job.Context.TryGetStruct<int>(SortItemsStepInstance.CtxCorrectKey, out int sortCorrect);
            job.Context.TryGetStruct<int>(SortItemsStepInstance.CtxTotalKey,   out int sortTotal);
            var msg = new JobFinishedMessage {
                instanceId     = job.InstanceId,
                terminalStatus = job.Status,
                failureReason  = job.FailureReason,
                rating         = rating,
                elapsedSeconds = job.ElapsedSeconds,
                correctCount   = sortCorrect,
                totalCount     = sortTotal,
                resultVariant  = variant,
                moneyEarned    = job.MoneyEarned,
                xpEarned       = job.XpEarned,
            };
            SendToOwner(job.OwnerNetId, msg);
            _toRemove.Add(job.InstanceId);
        }

        private void SendOffered(JobInstance job) {
            var ctx = job.Context;
            var stepDef = job.CurrentStepIndex < job.Definition.Steps.Count
                ? job.Definition.Steps[job.CurrentStepIndex]
                : null;

            var activeTarget = ResolveActiveTarget(job, stepDef);
            var msg = new JobOfferedMessage {
                instanceId = job.InstanceId,
                jobId = job.Definition.JobId,
                statusByte = (byte)job.Status,
                currentStepIndex = job.CurrentStepIndex,
                currentPromptKey = stepDef != null ? stepDef.PromptKey : string.Empty,
                currentTargetId = activeTarget?.TargetId ?? string.Empty,
                currentTargetName = activeTarget?.DisplayName ?? string.Empty,
                showTargetBeacon = stepDef != null && stepDef.ShowsTargetBeacon,
                primaryTargetKind = ctx.primaryTarget?.Kind ?? JobTargetKind.Zone,
                primaryTargetId   = ctx.primaryTarget?.TargetId ?? string.Empty,
                primaryTargetName = ctx.primaryTarget?.DisplayName ?? string.Empty,
                secondaryTargetKind = ctx.secondaryTarget?.Kind ?? JobTargetKind.Zone,
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
