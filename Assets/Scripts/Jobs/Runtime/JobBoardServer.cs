using System.Collections.Generic;
using Mirror;
using Sim.Logging;

namespace Sim.Jobs {
    /// <summary>
    /// Pilote l'aspect "panneau d'annonces partagé" du système. Plain C#
    /// singleton, souscrit aux JobEvents pour rebroadcaster un snapshot à
    /// chaque changement aux clients abonnés à la catégorie correspondante.
    ///
    /// Abonnement / désabonnement piloté par les messages JobBoardOpenMessage
    /// / JobBoardCloseMessage (JobSystemBootstrap branche les handlers).
    /// </summary>
    public class JobBoardServer {
        private static JobBoardServer _instance;
        public static JobBoardServer Instance => _instance ??= new JobBoardServer();

        private readonly Dictionary<JobCategory, HashSet<NetworkConnectionToClient>> _subscribers =
            new Dictionary<JobCategory, HashSet<NetworkConnectionToClient>>();

        private bool _subscribed;

        public void Subscribe() {
            if (_subscribed) return;
            JobEvents.JobPublished  += OnBoardChanged;
            JobEvents.JobTaken      += OnBoardChanged;
            JobEvents.StepAdvanced  += OnBoardChanged;
            JobEvents.JobCompleted  += OnBoardChanged;
            JobEvents.JobFailed     += OnBoardChanged;
            _subscribed = true;
        }

        public void Unsubscribe() {
            if (!_subscribed) return;
            JobEvents.JobPublished  -= OnBoardChanged;
            JobEvents.JobTaken      -= OnBoardChanged;
            JobEvents.StepAdvanced  -= OnBoardChanged;
            JobEvents.JobCompleted  -= OnBoardChanged;
            JobEvents.JobFailed     -= OnBoardChanged;
            _subscribed = false;
        }

        public void Reset() {
            Unsubscribe();
            _subscribers.Clear();
        }

        public void OpenBoard(NetworkConnectionToClient conn, JobCategory category) {
            if (conn == null) return;
            if (!_subscribers.TryGetValue(category, out var set)) {
                set = new HashSet<NetworkConnectionToClient>();
                _subscribers[category] = set;
            }
            set.Add(conn);
            SendSnapshot(conn, category);
        }

        public void CloseBoard(NetworkConnectionToClient conn, JobCategory category) {
            if (conn == null) return;
            if (_subscribers.TryGetValue(category, out var set)) set.Remove(conn);
        }

        public void OnPlayerDisconnected(NetworkConnectionToClient conn) {
            foreach (var set in _subscribers.Values) set.Remove(conn);
        }

        private void OnBoardChanged(JobInstance job) {
            if (job?.Definition == null) return;
            var cat = job.Definition.Category;
            if (!_subscribers.TryGetValue(cat, out var set) || set.Count == 0) return;

            var snapshot = BuildSnapshot(cat);
            foreach (var conn in set) {
                if (conn == null) continue;
                conn.Send(snapshot);
            }
        }

        private void SendSnapshot(NetworkConnectionToClient conn, JobCategory category) {
            var msg = BuildSnapshot(category);
            conn.Send(msg);
        }

        private JobBoardSnapshotMessage BuildSnapshot(JobCategory category) {
            var list = new List<JobBoardEntry>();
            foreach (var job in JobServerManager.Instance.Active) {
                if (job.Definition.Category != category) continue;
                if (!IsBoardRelevant(job.Status)) continue;
                list.Add(BuildEntry(job));
            }
            return new JobBoardSnapshotMessage {
                categoryByte = (byte)category,
                entries = list.ToArray()
            };
        }

        private static bool IsBoardRelevant(JobStatus s)
            => s == JobStatus.Available || s == JobStatus.Active;

        private static JobBoardEntry BuildEntry(JobInstance job) {
            return new JobBoardEntry {
                instanceId = job.InstanceId,
                jobId = job.Definition.JobId,
                statusByte = (byte)job.Status,
                currentStepIndex = job.CurrentStepIndex,
                ownerNetId = job.OwnerNetId,
                ownerName = ResolveOwnerName(job.OwnerNetId)
            };
        }

        private static string ResolveOwnerName(uint netId) {
            if (netId == 0u) return string.Empty;
            if (!NetworkServer.spawned.TryGetValue(netId, out var identity) || identity == null) return string.Empty;
            var player = identity.GetComponent<Sim.PlayerController>();
            if (player == null || player.CharacterData == null) return $"Player {netId}";
            var fullName = player.CharacterData.Identity.FullName;
            return string.IsNullOrEmpty(fullName) ? $"Player {netId}" : fullName;
        }
    }
}
