using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Wire le système de jobs dans le lifecycle de Mirror. À appeler depuis
    /// SimpleTownNetwork.OnStartServer/OnStopServer/OnStartClient/OnStopClient
    /// (même pattern que NpcSystemBootstrap).
    /// </summary>
    public static class MissionSystemBootstrap {
        private static GameObject _tickerGO;

        // ── Server ────────────────────────────────────────────────────────────

        public static void OnServerStart() {
            GameLogger.System.Info("MissionSystemServerStarting");

            MissionDatabase.Load();
            // RewardSystem must subscribe BEFORE MissionServerManager so rewards
            // apply (and accumulate on MissionInstance.MoneyEarned) before the
            // MissionFinishedMessage is built and sent.
            RewardSystem.Subscribe();
            MissionServerManager.Instance.Subscribe();
            MissionBoardServer.Instance.Subscribe();
            MissionItemCleanup.Subscribe();

            NetworkServer.RegisterHandler<MissionAcceptedMessage>(OnAcceptedFromClient);
            NetworkServer.RegisterHandler<MissionAbandonRequestMessage>(OnAbandonFromClient);
            NetworkServer.RegisterHandler<MissionBoardOpenMessage>(OnBoardOpenFromClient);
            NetworkServer.RegisterHandler<MissionBoardCloseMessage>(OnBoardCloseFromClient);
            NetworkServer.RegisterHandler<MissionBoardTakeMessage>(OnBoardTakeFromClient);
            NetworkServer.RegisterHandler<MissionUseMachineMessage>(OnUseMachineFromClient);
            NetworkServer.RegisterHandler<MissionSortDepositMessage>(OnSortDepositFromClient);
            NetworkServer.RegisterHandler<MissionChangeCareerMessage>(OnChangeCareerFromClient);

            _tickerGO = new GameObject("MissionServerTicker");
            Object.DontDestroyOnLoad(_tickerGO);
            _tickerGO.AddComponent<MissionServerTicker>();
            _tickerGO.AddComponent<PlayerCareerSalaryTicker>();

            GameLogger.System.Info("MissionSystemServerStarted");
        }

        public static void OnServerStop() {
            GameLogger.System.Info("MissionSystemServerStopping");

            NetworkServer.UnregisterHandler<MissionAcceptedMessage>();
            NetworkServer.UnregisterHandler<MissionAbandonRequestMessage>();
            NetworkServer.UnregisterHandler<MissionBoardOpenMessage>();
            NetworkServer.UnregisterHandler<MissionBoardCloseMessage>();
            NetworkServer.UnregisterHandler<MissionBoardTakeMessage>();
            NetworkServer.UnregisterHandler<MissionUseMachineMessage>();
            NetworkServer.UnregisterHandler<MissionSortDepositMessage>();
            NetworkServer.UnregisterHandler<MissionChangeCareerMessage>();

            if (_tickerGO != null) {
                Object.Destroy(_tickerGO);
                _tickerGO = null;
            }

            MissionItemCleanup.Unsubscribe();
            RewardSystem.Unsubscribe();
            MissionBoardServer.Instance.Reset();
            MissionServerManager.Instance.Reset();
            MissionTargetRegistry.Instance.Reset();
            GameLogger.System.Info("MissionSystemServerStopped");
        }

        private static void OnAcceptedFromClient(NetworkConnectionToClient conn, MissionAcceptedMessage msg)
            => MissionServerManager.Instance.Accept(msg.instanceId, conn);

        private static void OnAbandonFromClient(NetworkConnectionToClient conn, MissionAbandonRequestMessage msg)
            => MissionServerManager.Instance.Abandon(msg.instanceId, conn);

        private static void OnBoardOpenFromClient(NetworkConnectionToClient conn, MissionBoardOpenMessage msg)
            => MissionBoardServer.Instance.OpenBoard(conn, msg.professionId);

        private static void OnBoardCloseFromClient(NetworkConnectionToClient conn, MissionBoardCloseMessage msg)
            => MissionBoardServer.Instance.CloseBoard(conn, msg.professionId);

        private static void OnBoardTakeFromClient(NetworkConnectionToClient conn, MissionBoardTakeMessage msg)
            => MissionServerManager.Instance.TakeFromBoard(msg.instanceId, conn);

        private static void OnUseMachineFromClient(NetworkConnectionToClient conn, MissionUseMachineMessage msg)
            => UseMachineStepInstance.TryUseMachineFor(conn, msg.machineId, msg.snapshot);

        private static void OnSortDepositFromClient(NetworkConnectionToClient conn, MissionSortDepositMessage msg)
            => SortItemsStepInstance.TryDepositFor(conn, msg.binId);

        private static void OnChangeCareerFromClient(NetworkConnectionToClient conn, MissionChangeCareerMessage msg) {
            if (conn?.identity == null) return;
            var player = conn.identity.GetComponent<Sim.PlayerController>();
            if (player == null) return;
            player.StartCareerChange(msg.newProfessionId);
        }

        // ── Client ────────────────────────────────────────────────────────────

        public static void OnClientStart() {
            MissionDatabase.Load();
            MissionClientManager.Instance.RegisterHandlers();
            MissionBoardClient.Instance.RegisterHandlers();
            GameLogger.System.Info("MissionSystemClientStarted");
        }

        public static void OnClientStop() {
            MissionClientManager.Instance.UnregisterHandlers();
            MissionClientManager.Instance.ClearAll();
            MissionBoardClient.Instance.UnregisterHandlers();
            MissionBoardClient.Instance.ClearAll();
            GameLogger.System.Info("MissionSystemClientStopped");
        }
    }

    /// <summary>MonoBehaviour qui tick MissionServerManager chaque frame.</summary>
    public class MissionServerTicker : MonoBehaviour {
        private void Update() {
            if (!NetworkServer.active) return;
            MissionServerManager.Instance.Tick(Time.deltaTime);
        }
    }
}
