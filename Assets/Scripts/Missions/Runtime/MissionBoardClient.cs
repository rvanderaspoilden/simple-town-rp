using System;
using System.Collections.Generic;
using Mirror;

namespace Sim.Missions {
    /// <summary>
    /// Miroir client du board pour un métier donné (ProfessionConfig.id). Le HUD
    /// écoute BoardUpdated et redessine la liste. Push pur depuis le serveur.
    /// </summary>
    public class MissionBoardClient {
        private static MissionBoardClient _instance;
        public static MissionBoardClient Instance => _instance ??= new MissionBoardClient();

        private readonly Dictionary<string, MissionBoardEntry[]> _byProfession =
            new Dictionary<string, MissionBoardEntry[]>();

        private bool _handlersRegistered;

        public event Action<string, MissionBoardEntry[]> BoardUpdated;

        public void RegisterHandlers() {
            if (_handlersRegistered) return;
            NetworkClient.RegisterHandler<MissionBoardSnapshotMessage>(OnSnapshot);
            _handlersRegistered = true;
        }

        public void UnregisterHandlers() {
            if (!_handlersRegistered) return;
            NetworkClient.UnregisterHandler<MissionBoardSnapshotMessage>();
            _handlersRegistered = false;
        }

        public void ClearAll() {
            _byProfession.Clear();
        }

        public MissionBoardEntry[] GetEntries(string professionId)
            => !string.IsNullOrEmpty(professionId) && _byProfession.TryGetValue(professionId, out var arr)
                ? arr : System.Array.Empty<MissionBoardEntry>();

        public void RequestOpen(string professionId) {
            if (!NetworkClient.isConnected || string.IsNullOrEmpty(professionId)) return;
            NetworkClient.Send(new MissionBoardOpenMessage { professionId = professionId });
        }

        public void RequestClose(string professionId) {
            if (!NetworkClient.isConnected || string.IsNullOrEmpty(professionId)) return;
            NetworkClient.Send(new MissionBoardCloseMessage { professionId = professionId });
        }

        public void RequestTake(string instanceId) {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new MissionBoardTakeMessage { instanceId = instanceId });
        }

        private void OnSnapshot(MissionBoardSnapshotMessage msg) {
            _byProfession[msg.professionId] = msg.entries ?? System.Array.Empty<MissionBoardEntry>();
            BoardUpdated?.Invoke(msg.professionId, _byProfession[msg.professionId]);
        }
    }
}
