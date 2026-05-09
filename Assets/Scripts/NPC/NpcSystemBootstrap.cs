using Mirror;
using Sim.Logging;
using Sim.NPC;
using UnityEngine;

/// <summary>
/// Wire le système NPC dans le lifecycle de Mirror.
/// Appelé depuis SimpleTownNetwork.OnStartServer/OnStopServer/OnStartClient/OnStopClient.
/// </summary>
public static class NpcSystemBootstrap {
    private static GameObject _serverTickerGO;

    // ── Server ────────────────────────────────────────────────────────────────

    public static void OnServerStart() {
        GameLogger.Network.Info("NpcSystemServerStarting");

        PlayerRoomTracker.OnPlayerEnterRoom += Server_OnPlayerEnterRoom;
        PlayerRoomTracker.OnPlayerEnterRoom += RoomActivityController.Instance.HandlePlayerEnterRoom;
        PlayerRoomTracker.OnPlayerLeaveRoom += RoomActivityController.Instance.HandlePlayerLeaveRoom;

        // Charge la database de noms et configure le SpawnManager.
        NpcSpawnManager.Instance.NameDatabase =
            Resources.Load<NpcNameDatabase>("Configurations/Databases/NPC Name Database");
        if (NpcSpawnManager.Instance.NameDatabase == null) {
            GameLogger.Network.Warning("NpcNameDatabaseNotFound (fallback to procedural names)");
        }

        _serverTickerGO = new GameObject("NpcServerTicker");
        Object.DontDestroyOnLoad(_serverTickerGO);
        _serverTickerGO.AddComponent<NpcServerTicker>();

        GameLogger.Network.Info("NpcSystemServerStarted");
    }

    public static void OnServerStop() {
        GameLogger.Network.Info("NpcSystemServerStopping");

        PlayerRoomTracker.OnPlayerEnterRoom -= Server_OnPlayerEnterRoom;
        PlayerRoomTracker.OnPlayerEnterRoom -= RoomActivityController.Instance.HandlePlayerEnterRoom;
        PlayerRoomTracker.OnPlayerLeaveRoom -= RoomActivityController.Instance.HandlePlayerLeaveRoom;

        if (_serverTickerGO != null) {
            Object.Destroy(_serverTickerGO);
            _serverTickerGO = null;
        }

        NpcSpawnManager.Instance.Reset();   // retourne les NPC actifs au pool
        NpcPool.Instance.Dispose();         // détruit tous les GOs poolés
        NpcServerManager.Instance.Reset();
        InterestPointRegistry.Instance.Reset();
        RoomActivityController.Instance.Reset();
        GameLogger.Network.Info("NpcSystemServerStopped");
    }

    private static void Server_OnPlayerEnterRoom(NetworkConnectionToClient conn, string roomId) {
        NpcServerManager.Instance.SendRoomSnapshot(conn, roomId);
    }

    // ── Client ────────────────────────────────────────────────────────────────

    public static void OnClientStart() {
        ClientLogger.Network("NpcSystemClientStarting");

        if (ClientNpcManager.Instance == null) {
            ClientLogger.NetworkWarning("ClientNpcManagerMissing, creating automatically");
            var go = new GameObject("ClientNpcManager (AutoCreated)");
            go.AddComponent<ClientNpcManager>();
        }

        ClientNpcManager.Instance.RegisterHandlers();
        ClientLogger.Network("NpcSystemClientStarted");
    }

    public static void OnClientStop() {
        ClientLogger.Network("NpcSystemClientStopping");
        ClientNpcManager.Instance?.UnregisterHandlers();
        ClientNpcManager.Instance?.ClearAll();
        ClientLogger.Network("NpcSystemClientStopped");
    }
}

/// <summary>
/// MonoBehaviour qui pilote la cadence d'envoi serveur ET le tick du SpawnManager.
/// </summary>
public class NpcServerTicker : MonoBehaviour {
    private void Update() {
        if (!NetworkServer.active) return;
        NpcServerManager.Instance.Tick(Time.deltaTime);
        NpcSpawnManager.Instance.Tick(Time.deltaTime);
    }
}
