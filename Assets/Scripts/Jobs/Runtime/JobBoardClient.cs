using System;
using System.Collections.Generic;
using Mirror;

namespace Sim.Jobs {
    /// <summary>
    /// Miroir client du board pour une catégorie donnée. Le HUD écoute
    /// l'event BoardUpdated et redessine la liste. Pas de polling — push
    /// pur depuis le serveur (JobBoardServer rebroadcast à chaque event).
    /// </summary>
    public class JobBoardClient {
        private static JobBoardClient _instance;
        public static JobBoardClient Instance => _instance ??= new JobBoardClient();

        private readonly Dictionary<JobCategory, JobBoardEntry[]> _byCategory =
            new Dictionary<JobCategory, JobBoardEntry[]>();

        private bool _handlersRegistered;

        public event Action<JobCategory, JobBoardEntry[]> BoardUpdated;

        public void RegisterHandlers() {
            if (_handlersRegistered) return;
            NetworkClient.RegisterHandler<JobBoardSnapshotMessage>(OnSnapshot);
            _handlersRegistered = true;
        }

        public void UnregisterHandlers() {
            if (!_handlersRegistered) return;
            NetworkClient.UnregisterHandler<JobBoardSnapshotMessage>();
            _handlersRegistered = false;
        }

        public void ClearAll() {
            _byCategory.Clear();
        }

        public JobBoardEntry[] GetEntries(JobCategory category)
            => _byCategory.TryGetValue(category, out var arr) ? arr : System.Array.Empty<JobBoardEntry>();

        public void RequestOpen(JobCategory category) {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new JobBoardOpenMessage { categoryByte = (byte)category });
        }

        public void RequestClose(JobCategory category) {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new JobBoardCloseMessage { categoryByte = (byte)category });
        }

        public void RequestTake(string instanceId) {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new JobBoardTakeMessage { instanceId = instanceId });
        }

        private void OnSnapshot(JobBoardSnapshotMessage msg) {
            _byCategory[msg.Category] = msg.entries ?? System.Array.Empty<JobBoardEntry>();
            BoardUpdated?.Invoke(msg.Category, _byCategory[msg.Category]);
        }
    }
}
