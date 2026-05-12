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
            var job = JobInstance.CreatePublished(def, context);
            _byInstanceId[job.InstanceId] = job;
            JobEvents.RaiseJobPublished(job);
            return job;
        }

        public bool TakeFromBoard(string instanceId, NetworkConnectionToClient sender) {
            if (!_byInstanceId.TryGetValue(instanceId, out var job)) return false;
            if (sender == null || sender.identity == null) return false;
            if (!job.IsAvailable) return false;

            uint netId = sender.identity.netId;
            int active = ActiveCountForOwner(netId);
            if (active >= job.Definition.MaxConcurrentPerPlayer) {
                GameLogger.System.Debug("JobTakeDenied_MaxConcurrent {JobId} {NetId} {Active}",
                    job.Definition.JobId, netId, active);
                return false;
            }

            if (!job.Take(netId)) return false;
            AddToOwnerIndex(netId, job);
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
            return true;
        }

        public bool Abandon(string instanceId, NetworkConnectionToClient sender) {
            if (!_byInstanceId.TryGetValue(instanceId, out var job)) return false;
            if (sender == null || sender.identity == null) return false;
            if (sender.identity.netId != job.OwnerNetId) return false;

            job.Abandon();
            return true;
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
                if (job.Status == JobStatus.Active) job.Tick(dt);
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

        private void OnStepAdvanced(JobInstance job) {
            var stepDef = job.CurrentStepIndex < job.Definition.Steps.Count
                ? job.Definition.Steps[job.CurrentStepIndex]
                : null;

            var msg = new JobStepAdvancedMessage {
                instanceId = job.InstanceId,
                newStepIndex = job.CurrentStepIndex,
                promptKey = stepDef != null ? stepDef.PromptKey : string.Empty
            };
            SendToOwner(job.OwnerNetId, msg);
        }

        private void OnJobFinished(JobInstance job) {
            var msg = new JobFinishedMessage {
                instanceId = job.InstanceId,
                terminalStatus = job.Status,
                failureReason = job.FailureReason
            };
            SendToOwner(job.OwnerNetId, msg);
            _toRemove.Add(job.InstanceId);
        }

        private void SendOffered(JobInstance job) {
            var ctx = job.Context;
            var msg = new JobOfferedMessage {
                instanceId = job.InstanceId,
                jobId = job.Definition.JobId,
                primaryTargetKind = ctx.primaryTarget?.Kind ?? JobTargetKind.Zone,
                primaryTargetId   = ctx.primaryTarget?.TargetId ?? string.Empty,
                secondaryTargetKind = ctx.secondaryTarget?.Kind ?? JobTargetKind.Zone,
                secondaryTargetId   = ctx.secondaryTarget?.TargetId ?? string.Empty,
                payloadItemId = ctx.payloadItemId ?? string.Empty
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
