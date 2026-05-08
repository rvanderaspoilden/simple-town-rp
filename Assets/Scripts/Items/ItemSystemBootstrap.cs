using Mirror;

/// <summary>
/// Wires up the item system on server/client start and stop.
/// Called from SimpleTownNetwork alongside PropSystemBootstrap and NpcSystemBootstrap.
/// </summary>
public static class ItemSystemBootstrap
{
    public static void OnServerStart()
    {
        NetworkServer.RegisterHandler<C2S_RequestPickupItem>(ServerItemManager.Instance.HandlePickup);
        NetworkServer.RegisterHandler<C2S_RequestDropItem>(ServerItemManager.Instance.HandleDrop);
        NetworkServer.RegisterHandler<C2S_RequestSwapHands>(ServerItemManager.Instance.HandleSwap);
        NetworkServer.RegisterHandler<C2S_AdminSpawnItem>(ServerItemManager.Instance.HandleAdminSpawn);

        PlayerRoomTracker.OnPlayerEnterRoom += ServerItemManager.Instance.OnPlayerEnterRoom;
        PlayerRoomTracker.OnPlayerLeaveRoom += ServerItemManager.Instance.OnPlayerLeaveRoom;
        SimpleTownNetwork.OnPlayerDisconnected += ServerItemManager.Instance.OnPlayerDisconnect;
    }

    public static void OnClientStart()
    {
        ClientItemManager.Instance.RegisterHandlers();
    }

    public static void OnServerStop()
    {
        NetworkServer.UnregisterHandler<C2S_RequestPickupItem>();
        NetworkServer.UnregisterHandler<C2S_RequestDropItem>();
        NetworkServer.UnregisterHandler<C2S_RequestSwapHands>();
        NetworkServer.UnregisterHandler<C2S_AdminSpawnItem>();

        PlayerRoomTracker.OnPlayerEnterRoom -= ServerItemManager.Instance.OnPlayerEnterRoom;
        PlayerRoomTracker.OnPlayerLeaveRoom -= ServerItemManager.Instance.OnPlayerLeaveRoom;
        SimpleTownNetwork.OnPlayerDisconnected -= ServerItemManager.Instance.OnPlayerDisconnect;

        ServerItemManager.Instance.Reset();
    }

    public static void OnClientStop()
    {
        ClientItemManager.Instance.UnregisterHandlers();
        ClientItemManager.Instance.Reset();
    }
}
