using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Wire le système de jobs dans le lifecycle de Mirror. À appeler depuis
    /// SimpleTownNetwork.OnStartServer/OnStopServer/OnStartClient/OnStopClient
    /// (même pattern que NpcSystemBootstrap).
    /// </summary>
    public static class JobSystemBootstrap {
        private static GameObject _tickerGO;

        // ── Server ────────────────────────────────────────────────────────────

        public static void OnServerStart() {
            GameLogger.System.Info("JobSystemServerStarting");

            JobDatabase.Load();
            JobServerManager.Instance.Subscribe();
            JobBoardServer.Instance.Subscribe();
            RewardSystem.Subscribe();
            JobItemCleanup.Subscribe();

            NetworkServer.RegisterHandler<JobAcceptedMessage>(OnAcceptedFromClient);
            NetworkServer.RegisterHandler<JobAbandonRequestMessage>(OnAbandonFromClient);
            NetworkServer.RegisterHandler<JobBoardOpenMessage>(OnBoardOpenFromClient);
            NetworkServer.RegisterHandler<JobBoardCloseMessage>(OnBoardCloseFromClient);
            NetworkServer.RegisterHandler<JobBoardTakeMessage>(OnBoardTakeFromClient);
            NetworkServer.RegisterHandler<JobUseMachineMessage>(OnUseMachineFromClient);
            NetworkServer.RegisterHandler<JobChangeCareerMessage>(OnChangeCareerFromClient);

            _tickerGO = new GameObject("JobServerTicker");
            Object.DontDestroyOnLoad(_tickerGO);
            _tickerGO.AddComponent<JobServerTicker>();
            _tickerGO.AddComponent<PlayerCareerSalaryTicker>();

            GameLogger.System.Info("JobSystemServerStarted");
        }

        public static void OnServerStop() {
            GameLogger.System.Info("JobSystemServerStopping");

            NetworkServer.UnregisterHandler<JobAcceptedMessage>();
            NetworkServer.UnregisterHandler<JobAbandonRequestMessage>();
            NetworkServer.UnregisterHandler<JobBoardOpenMessage>();
            NetworkServer.UnregisterHandler<JobBoardCloseMessage>();
            NetworkServer.UnregisterHandler<JobBoardTakeMessage>();
            NetworkServer.UnregisterHandler<JobUseMachineMessage>();
            NetworkServer.UnregisterHandler<JobChangeCareerMessage>();

            if (_tickerGO != null) {
                Object.Destroy(_tickerGO);
                _tickerGO = null;
            }

            JobItemCleanup.Unsubscribe();
            RewardSystem.Unsubscribe();
            JobBoardServer.Instance.Reset();
            JobServerManager.Instance.Reset();
            JobTargetRegistry.Instance.Reset();
            GameLogger.System.Info("JobSystemServerStopped");
        }

        private static void OnAcceptedFromClient(NetworkConnectionToClient conn, JobAcceptedMessage msg)
            => JobServerManager.Instance.Accept(msg.instanceId, conn);

        private static void OnAbandonFromClient(NetworkConnectionToClient conn, JobAbandonRequestMessage msg)
            => JobServerManager.Instance.Abandon(msg.instanceId, conn);

        private static void OnBoardOpenFromClient(NetworkConnectionToClient conn, JobBoardOpenMessage msg)
            => JobBoardServer.Instance.OpenBoard(conn, msg.Category);

        private static void OnBoardCloseFromClient(NetworkConnectionToClient conn, JobBoardCloseMessage msg)
            => JobBoardServer.Instance.CloseBoard(conn, msg.Category);

        private static void OnBoardTakeFromClient(NetworkConnectionToClient conn, JobBoardTakeMessage msg)
            => JobServerManager.Instance.TakeFromBoard(msg.instanceId, conn);

        private static void OnUseMachineFromClient(NetworkConnectionToClient conn, JobUseMachineMessage msg)
            => UseMachineStepInstance.TryUseMachineFor(conn, msg.machineId);

        private static void OnChangeCareerFromClient(NetworkConnectionToClient conn, JobChangeCareerMessage msg) {
            if (conn?.identity == null) return;
            var player = conn.identity.GetComponent<Sim.PlayerController>();
            if (player == null) return;
            player.StartCareerChange(msg.newJob);
        }

        // ── Client ────────────────────────────────────────────────────────────

        public static void OnClientStart() {
            JobDatabase.Load();
            JobClientManager.Instance.RegisterHandlers();
            JobBoardClient.Instance.RegisterHandlers();
            GameLogger.System.Info("JobSystemClientStarted");
        }

        public static void OnClientStop() {
            JobClientManager.Instance.UnregisterHandlers();
            JobClientManager.Instance.ClearAll();
            JobBoardClient.Instance.UnregisterHandlers();
            JobBoardClient.Instance.ClearAll();
            GameLogger.System.Info("JobSystemClientStopped");
        }
    }

    /// <summary>MonoBehaviour qui tick JobServerManager chaque frame.</summary>
    public class JobServerTicker : MonoBehaviour {
        private void Update() {
            if (!NetworkServer.active) return;
            JobServerManager.Instance.Tick(Time.deltaTime);
        }
    }
}
