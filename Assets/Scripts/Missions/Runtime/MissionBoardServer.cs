using System.Collections.Generic;
using Mirror;
using Sim.Logging;

namespace Sim.Missions {
    /// <summary>
    /// Pilote l'aspect "panneau d'annonces partagé" du système. Plain C#
    /// singleton, souscrit aux MissionEvents pour rebroadcaster un snapshot à
    /// chaque changement aux clients abonnés au métier correspondant (ProfessionConfig.id).
    /// </summary>
    public class MissionBoardServer {
        private static MissionBoardServer _instance;
        public static MissionBoardServer Instance => _instance ??= new MissionBoardServer();

        private readonly Dictionary<string, HashSet<NetworkConnectionToClient>> _subscribers =
            new Dictionary<string, HashSet<NetworkConnectionToClient>>();

        private bool _subscribed;

        public void Subscribe() {
            if (_subscribed) return;
            MissionEvents.MissionPublished  += OnBoardChanged;
            MissionEvents.MissionTaken      += OnBoardChanged;
            MissionEvents.StepAdvanced  += OnBoardChanged;
            MissionEvents.MissionCompleted  += OnBoardChanged;
            MissionEvents.MissionFailed     += OnBoardChanged;
            _subscribed = true;
        }

        public void Unsubscribe() {
            if (!_subscribed) return;
            MissionEvents.MissionPublished  -= OnBoardChanged;
            MissionEvents.MissionTaken      -= OnBoardChanged;
            MissionEvents.StepAdvanced  -= OnBoardChanged;
            MissionEvents.MissionCompleted  -= OnBoardChanged;
            MissionEvents.MissionFailed     -= OnBoardChanged;
            _subscribed = false;
        }

        public void Reset() {
            Unsubscribe();
            _subscribers.Clear();
        }

        public void OpenBoard(NetworkConnectionToClient conn, string professionId) {
            if (conn == null || string.IsNullOrEmpty(professionId)) return;

            // Career gate: only workers of the right job may subscribe to a
            // profession's board. Also enforced on Take (defense in depth).
            if (!HasJob(conn, professionId)) {
                GameLogger.System.Debug("MissionBoardOpenDenied_WrongJob {NetId} {Profession}",
                    conn.identity != null ? conn.identity.netId : 0u, professionId);
                conn.Send(new MissionNotificationMessage {
                    text = "Tu n'es pas employé pour ce métier."
                });
                return;
            }

            if (!_subscribers.TryGetValue(professionId, out var set)) {
                set = new HashSet<NetworkConnectionToClient>();
                _subscribers[professionId] = set;
            }
            set.Add(conn);
            SendSnapshot(conn, professionId);
        }

        private static bool HasJob(NetworkConnectionToClient conn, string professionId) {
            if (conn?.identity == null) return false;
            var player = conn.identity.GetComponent<Sim.PlayerController>();
            if (player == null || player.CharacterData == null) return false;
            return player.CharacterData.CurrentProfessionId == professionId;
        }

        public void CloseBoard(NetworkConnectionToClient conn, string professionId) {
            if (conn == null || string.IsNullOrEmpty(professionId)) return;
            if (_subscribers.TryGetValue(professionId, out var set)) set.Remove(conn);
        }

        public void OnPlayerDisconnected(NetworkConnectionToClient conn) {
            foreach (var set in _subscribers.Values) set.Remove(conn);
        }

        private void OnBoardChanged(MissionInstance job) {
            if (job?.Definition == null) return;
            var professionId = job.Definition.ProfessionId;
            if (string.IsNullOrEmpty(professionId)) return;
            if (!_subscribers.TryGetValue(professionId, out var set) || set.Count == 0) return;

            var snapshot = BuildSnapshot(professionId);
            foreach (var conn in set) {
                if (conn == null) continue;
                conn.Send(snapshot);
            }
        }

        private void SendSnapshot(NetworkConnectionToClient conn, string professionId) {
            var msg = BuildSnapshot(professionId);
            conn.Send(msg);
        }

        private MissionBoardSnapshotMessage BuildSnapshot(string professionId) {
            var list = new List<MissionBoardEntry>();
            foreach (var job in MissionServerManager.Instance.Active) {
                if (job.Definition.ProfessionId != professionId) continue;
                if (!IsBoardRelevant(job.Status)) continue;
                list.Add(BuildEntry(job));
            }
            return new MissionBoardSnapshotMessage {
                professionId = professionId,
                entries = list.ToArray()
            };
        }

        private static bool IsBoardRelevant(MissionStatus s)
            => s == MissionStatus.Available || s == MissionStatus.Active;

        private static MissionBoardEntry BuildEntry(MissionInstance job) {
            return new MissionBoardEntry {
                instanceId = job.InstanceId,
                missionId = job.Definition.MissionId,
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
