using Mirror;
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
        NetworkServer.RegisterHandler<C2S_EnterRoom>      (Server_OnEnterRoom);
        NetworkServer.RegisterHandler<C2S_LeaveRoom>      (Server_OnLeaveRoom);
        NetworkServer.RegisterHandler<C2S_PropInteraction>(Server_OnInteraction);
        NetworkServer.RegisterHandler<C2S_BuildProp>      (Server_OnBuildProp);
        NetworkServer.RegisterHandler<C2S_EditProp>       (Server_OnEditProp);
        NetworkServer.RegisterHandler<C2S_RemoveProp>     (Server_OnRemoveProp);

        SimpleTownNetwork.OnPlayerDisconnected += Server_OnDisconnect;

        Debug.Log("[PropSystem] Server handlers registered");
    }

    public static void OnServerStop() {
        NetworkServer.UnregisterHandler<C2S_EnterRoom>();
        NetworkServer.UnregisterHandler<C2S_LeaveRoom>();
        NetworkServer.UnregisterHandler<C2S_PropInteraction>();
        NetworkServer.UnregisterHandler<C2S_BuildProp>();
        NetworkServer.UnregisterHandler<C2S_EditProp>();
        NetworkServer.UnregisterHandler<C2S_RemoveProp>();

        SimpleTownNetwork.OnPlayerDisconnected -= Server_OnDisconnect;

        ServerPropManager.Instance.Reset();
        PlayerRoomTracker.Instance.Reset();

        Debug.Log("[PropSystem] Server handlers unregistered");
    }

    // ── Client ────────────────────────────────────────────────────────────────

    public static void OnClientStart() {
        // Auto-create ClientPropManager if missing
        if (ClientPropManager.Instance == null) {
            Debug.LogWarning("[PropSystem] ClientPropManager.Instance is null! Creating automatically...");
            var go = new GameObject("ClientPropManager (AutoCreated)");
            go.AddComponent<ClientPropManager>();
        }

        ClientPropManager.Instance.RegisterHandlers();
        Debug.Log("[PropSystem] Client handlers registered");
    }

    public static void OnClientStop() {
        ClientPropManager.Instance?.UnregisterHandlers();
        Debug.Log("[PropSystem] Client handlers unregistered");
    }

    // ── Server message handlers ───────────────────────────────────────────────

    private static void Server_OnEnterRoom(NetworkConnectionToClient conn, C2S_EnterRoom msg) {
        if (string.IsNullOrEmpty(msg.RoomId)) return;

        PlayerRoomTracker.Instance.EnterRoom(conn, msg.RoomId);
        ServerPropManager.Instance.SendRoomSnapshot(conn, msg.RoomId);

        Debug.Log($"[PropSystem] Player {conn.connectionId} entered room '{msg.RoomId}'");
    }

    private static void Server_OnLeaveRoom(NetworkConnectionToClient conn, C2S_LeaveRoom msg) {
        PlayerRoomTracker.Instance.LeaveRoom(conn);
        Debug.Log($"[PropSystem] Player {conn.connectionId} left room '{msg.RoomId}'");
    }

    private static void Server_OnDisconnect(NetworkConnectionToClient conn) {
        // Free any seat/couch slots held by the disconnecting player before removing room tracking
        if (conn.identity != null) {
            uint   netId  = conn.identity.netId;
            string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
            if (!string.IsNullOrEmpty(roomId))
                PropInteractionRouter.ReleaseSeatsByPlayer(roomId, netId);
        }
        PlayerRoomTracker.Instance.OnDisconnect(conn);
    }

    private static void Server_OnInteraction(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        // Verify the sender is actually in the room they claim
        string playerRoom = PlayerRoomTracker.Instance.GetRoom(conn);
        if (playerRoom != msg.RoomId) {
            Debug.LogWarning($"[PropSystem] Conn {conn.connectionId} sent interaction for room '{msg.RoomId}' but is in '{playerRoom}'");
            return;
        }

        PropInteractionRouter.Route(conn, msg);
    }

    private static void Server_OnBuildProp(NetworkConnectionToClient conn, C2S_BuildProp msg) {
        if (PlayerRoomTracker.Instance.GetRoom(conn) != msg.RoomId) {
            Debug.LogWarning($"[PropSystem] Conn {conn.connectionId} BuildProp room mismatch");
            return;
        }
        PropInteractionRouter.HandleBuildProp(conn, msg);
    }

    private static void Server_OnEditProp(NetworkConnectionToClient conn, C2S_EditProp msg) {
        if (PlayerRoomTracker.Instance.GetRoom(conn) != msg.RoomId) {
            Debug.LogWarning($"[PropSystem] Conn {conn.connectionId} EditProp room mismatch");
            return;
        }
        PropInteractionRouter.HandleEditProp(conn, msg);
    }

    private static void Server_OnRemoveProp(NetworkConnectionToClient conn, C2S_RemoveProp msg) {
        if (PlayerRoomTracker.Instance.GetRoom(conn) != msg.RoomId) {
            Debug.LogWarning($"[PropSystem] Conn {conn.connectionId} RemoveProp room mismatch");
            return;
        }
        PropInteractionRouter.HandleRemoveProp(conn, msg);
    }
}
