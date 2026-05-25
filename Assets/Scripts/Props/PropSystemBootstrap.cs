using Mirror;
using Sim.Logging;
using UnityEngine;

/// <summary>
/// Wires the prop system into Mirror's lifecycle.
/// Call the static methods from SimpleTownNetwork.OnStartServer / OnStopServer /
/// OnStartClient / OnStopClient.
///
/// This is the ONLY file in the prop system that touches Mirror registration.
/// Keep it thin — all logic lives in ServerPropManager / ClientPropManager.
/// </summary>
public static class PropSystemBootstrap {
    // ── Server ────────────────────────────────────────────────────────────────

    public static void OnServerStart() {
        GameLogger.Network.Info("PropSystemServerStarting");

        // Auto-create PropInteractionDispatcher if missing from the scene.
        if (PropInteractionDispatcher.Instance == null) {
            GameLogger.Network.Warning("PropInteractionDispatcherMissing, creating automatically");
            var go = new GameObject("PropInteractionDispatcher (AutoCreated)");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<PropInteractionDispatcher>();
        }

        NetworkServer.RegisterHandler<C2S_EnterRoom>      (Server_OnEnterRoom);
        NetworkServer.RegisterHandler<C2S_LeaveRoom>      (Server_OnLeaveRoom);
        NetworkServer.RegisterHandler<C2S_PropInteraction>(Server_OnInteraction);
        NetworkServer.RegisterHandler<C2S_BuildProp>      (Server_OnBuildProp);
        NetworkServer.RegisterHandler<C2S_EditProp>       (Server_OnEditProp);
        NetworkServer.RegisterHandler<C2S_RemoveProp>     (Server_OnRemoveProp);
        NetworkServer.RegisterHandler<C2S_DestroyProp>    (Server_OnDestroyProp);
        NetworkServer.RegisterHandler<C2S_SetPropForSale> (Server_OnSetPropForSale);
        NetworkServer.RegisterHandler<C2S_UnlistProp>     (Server_OnUnlistProp);
        NetworkServer.RegisterHandler<C2S_BuyProp>        (Server_OnBuyProp);
        NetworkServer.RegisterHandler<C2S_TeleporterUse>  (Server_OnTeleporterUse);
        NetworkServer.RegisterHandler<C2S_ApplyWallCovers>(Server_OnApplyWallCovers);
        NetworkServer.RegisterHandler<C2S_ApplyGroundCovers>(Server_OnApplyGroundCovers);

        SimpleTownNetwork.OnPlayerDisconnected += Server_OnDisconnect;

        GameLogger.Network.Info("PropSystemServerStarted {HandlerCount}", 10);
    }

    public static void OnServerStop() {
        GameLogger.Network.Info("PropSystemServerStopping");
        
        NetworkServer.UnregisterHandler<C2S_EnterRoom>();
        NetworkServer.UnregisterHandler<C2S_LeaveRoom>();
        NetworkServer.UnregisterHandler<C2S_PropInteraction>();
        NetworkServer.UnregisterHandler<C2S_BuildProp>();
        NetworkServer.UnregisterHandler<C2S_EditProp>();
        NetworkServer.UnregisterHandler<C2S_RemoveProp>();
        NetworkServer.UnregisterHandler<C2S_DestroyProp>();
        NetworkServer.UnregisterHandler<C2S_SetPropForSale>();
        NetworkServer.UnregisterHandler<C2S_UnlistProp>();
        NetworkServer.UnregisterHandler<C2S_BuyProp>();
        NetworkServer.UnregisterHandler<C2S_TeleporterUse>();
        NetworkServer.UnregisterHandler<C2S_ApplyWallCovers>();
        NetworkServer.UnregisterHandler<C2S_ApplyGroundCovers>();

        SimpleTownNetwork.OnPlayerDisconnected -= Server_OnDisconnect;

        ServerPropManager.Instance.Reset();
        PlayerRoomTracker.Instance.Reset();

        GameLogger.Network.Info("PropSystemServerStopped");
    }

    // ── Client ────────────────────────────────────────────────────────────────

    public static void OnClientStart() {
        ClientLogger.Network("PropSystemClientStarting");
        
        // Auto-create ClientPropManager if missing
        if (ClientPropManager.Instance == null) {
            ClientLogger.NetworkWarning("ClientPropManagerMissing, creating automatically");
            var go = new GameObject("ClientPropManager (AutoCreated)");
            go.AddComponent<ClientPropManager>();
        }

        ClientPropManager.Instance.RegisterHandlers();
        ClientLogger.Network("PropSystemClientStarted");
    }

    public static void OnClientStop() {
        ClientLogger.Network("PropSystemClientStopping");
        ClientPropManager.Instance?.UnregisterHandlers();
        ClientLogger.Network("PropSystemClientStopped");
    }

    // ── Server message handlers ───────────────────────────────────────────────

    private static void Server_OnEnterRoom(NetworkConnectionToClient conn, C2S_EnterRoom msg) {
        if (string.IsNullOrEmpty(msg.RoomId)) {
            GameLogger.Network.Warning("EnterRoomEmptyRoomId {ConnectionId}", conn.connectionId);
            return;
        }

        GameLogger.Network.Info("EnterRoom {ConnectionId} {RoomId} {PlayerNetId}",
            conn.connectionId, msg.RoomId, conn.identity?.netId ?? 0);
        
        PlayerRoomTracker.Instance.EnterRoom(conn, msg.RoomId);
        ServerPropManager.Instance.SendRoomSnapshot(conn, msg.RoomId);

        GameLogger.Network.Debug("RoomSnapshotSent {ConnectionId} {RoomId}", conn.connectionId, msg.RoomId);
    }

    private static void Server_OnLeaveRoom(NetworkConnectionToClient conn, C2S_LeaveRoom msg) {
        GameLogger.Network.Info("LeaveRoom {ConnectionId} {RoomId}", conn.connectionId, msg.RoomId);
        PlayerRoomTracker.Instance.LeaveRoom(conn);
    }

    private static void Server_OnDisconnect(NetworkConnectionToClient conn) {
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        GameLogger.Network.Info("PropSystemPlayerDisconnect {ConnectionId} {RoomId} {PlayerNetId}",
            conn.connectionId, roomId ?? "none", conn.identity?.netId ?? 0);
        
        // Free any seat/couch slots held by the disconnecting player before removing room tracking
        if (conn.identity != null) {
            uint netId = conn.identity.netId;
            if (!string.IsNullOrEmpty(roomId))
                PropInteractionRouter.ReleaseSeatsByPlayer(roomId, netId);
        }
        PlayerRoomTracker.Instance.OnDisconnect(conn);
    }

    private static void Server_OnInteraction(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        // Verify the sender is actually in the room they claim
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            GameLogger.Network.Warning("PropInteractionRoomMismatch {ConnectionId} {ClaimedRoom} {ActualRoom} {PropId}",
                conn.connectionId, msg.RoomId, playerRoom ?? "none", msg.PropId);
            return;
        }

        GameLogger.Network.Debug("PropInteraction {ConnectionId} {RoomId} {PropId} {Type}",
            conn.connectionId, msg.RoomId, msg.PropId, msg.Type);
        
        PropInteractionRouter.Route(conn, msg);
    }

    private static void Server_OnBuildProp(NetworkConnectionToClient conn, C2S_BuildProp msg) {
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            GameLogger.Network.Warning("BuildPropRoomMismatch {ConnectionId} {ClaimedRoom} {ActualRoom}",
                conn.connectionId, msg.RoomId, playerRoom ?? "none");
            return;
        }
        
        GameLogger.Network.Info("BuildProp {ConnectionId} {RoomId} {PropConfigId} {Position}",
            conn.connectionId, msg.RoomId, msg.PropConfigId, msg.Position);
        PropInteractionRouter.HandleBuildProp(conn, msg);
    }

    private static void Server_OnEditProp(NetworkConnectionToClient conn, C2S_EditProp msg) {
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            GameLogger.Network.Warning("EditPropRoomMismatch {ConnectionId} {ClaimedRoom} {ActualRoom} {PropId}",
                conn.connectionId, msg.RoomId, playerRoom ?? "none", msg.PropId);
            return;
        }
        
        GameLogger.Network.Info("EditProp {ConnectionId} {RoomId} {PropId} {Position}",
            conn.connectionId, msg.RoomId, msg.PropId, msg.Position);
        PropInteractionRouter.HandleEditProp(conn, msg);
    }

    private static void Server_OnRemoveProp(NetworkConnectionToClient conn, C2S_RemoveProp msg) {
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            GameLogger.Network.Warning("RemovePropRoomMismatch {ConnectionId} {ClaimedRoom} {ActualRoom} {PropId}",
                conn.connectionId, msg.RoomId, playerRoom ?? "none", msg.PropId);
            return;
        }
        
        GameLogger.Network.Info("RemoveProp {ConnectionId} {RoomId} {PropId}",
            conn.connectionId, msg.RoomId, msg.PropId);
        PropInteractionRouter.HandleRemoveProp(conn, msg);
    }

    private static void Server_OnDestroyProp(NetworkConnectionToClient conn, C2S_DestroyProp msg) {
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            GameLogger.Network.Warning("DestroyPropRoomMismatch {ConnectionId} {ClaimedRoom} {ActualRoom} {PropId}",
                conn.connectionId, msg.RoomId, playerRoom ?? "none", msg.PropId);
            return;
        }

        GameLogger.Network.Info("DestroyProp {ConnectionId} {RoomId} {PropId}",
            conn.connectionId, msg.RoomId, msg.PropId);
        PropInteractionRouter.HandleDestroyProp(conn, msg);
    }

    private static void Server_OnSetPropForSale(NetworkConnectionToClient conn, C2S_SetPropForSale msg) {
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            GameLogger.Network.Warning("SetForSaleRoomMismatch {ConnectionId} {ClaimedRoom} {ActualRoom} {PropId}",
                conn.connectionId, msg.RoomId, playerRoom ?? "none", msg.PropId);
            return;
        }
        GameLogger.Network.Info("SetPropForSale {ConnectionId} {RoomId} {PropId} {Price}",
            conn.connectionId, msg.RoomId, msg.PropId, msg.Price);
        PropInteractionRouter.HandleSetForSale(conn, msg);
    }

    private static void Server_OnUnlistProp(NetworkConnectionToClient conn, C2S_UnlistProp msg) {
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            GameLogger.Network.Warning("UnlistRoomMismatch {ConnectionId} {ClaimedRoom} {ActualRoom} {PropId}",
                conn.connectionId, msg.RoomId, playerRoom ?? "none", msg.PropId);
            return;
        }
        GameLogger.Network.Info("UnlistProp {ConnectionId} {RoomId} {PropId}",
            conn.connectionId, msg.RoomId, msg.PropId);
        PropInteractionRouter.HandleUnlist(conn, msg);
    }

    private static void Server_OnBuyProp(NetworkConnectionToClient conn, C2S_BuyProp msg) {
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            GameLogger.Network.Warning("BuyPropRoomMismatch {ConnectionId} {ClaimedRoom} {ActualRoom} {PropId}",
                conn.connectionId, msg.RoomId, playerRoom ?? "none", msg.PropId);
            return;
        }
        GameLogger.Network.Info("BuyProp {ConnectionId} {RoomId} {PropId}",
            conn.connectionId, msg.RoomId, msg.PropId);
        PropInteractionRouter.HandleBuyProp(conn, msg);
    }

    private static void Server_OnTeleporterUse(NetworkConnectionToClient conn, C2S_TeleporterUse msg) {
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        GameLogger.Network.Info("TeleporterUse {ConnectionId} {RoomId} {FloorDestination}",
            conn.connectionId, roomId ?? "unknown", msg.FloorDestination);
        PropInteractionDispatcher.Instance?.HandleTeleporterUse(conn, msg.FloorDestination);
    }

    private static void Server_OnApplyWallCovers(NetworkConnectionToClient conn, C2S_ApplyWallCovers msg) {
        GameLogger.Network.Info("ApplyWallCovers {ConnectionId} {RoomId} {JsonLength}",
            conn.connectionId, msg.RoomId, msg.CoversJson?.Length ?? 0);
        PropInteractionDispatcher.Instance?.HandleApplyWallCovers(conn, msg.RoomId, msg.CoversJson);
    }

    private static void Server_OnApplyGroundCovers(NetworkConnectionToClient conn, C2S_ApplyGroundCovers msg) {
        GameLogger.Network.Info("ApplyGroundCovers {ConnectionId} {RoomId} {JsonLength}",
            conn.connectionId, msg.RoomId, msg.CoversJson?.Length ?? 0);
        PropInteractionDispatcher.Instance?.HandleApplyGroundCovers(conn, msg.RoomId, msg.CoversJson);
    }
}
