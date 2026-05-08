using System.Collections.Generic;
using Mirror;
using Sim;
using Sim.Logging;
using UnityEngine;

/// <summary>
/// Authoritative server-side store for all world items grouped by room.
/// Mirror is used only as a transport — no SyncVar, no NetworkBehaviour, no NetworkIdentity.
///
/// Lifecycle:
///   SpawnItem   : create a world item in a room, broadcast S2C_SpawnItem to room members.
///   HandlePickup: validate and attach an item to a player's hand.
///   HandleDrop  : detach an item from a player's hand, drop at world position.
///   HandleSwap  : swap left/right hand assignments for a player.
///   SendRoomSnapshot: send all items in a room to a late-joining client.
/// </summary>
public class ServerItemManager
{
    private static ServerItemManager _instance;
    public static ServerItemManager Instance => _instance ??= new ServerItemManager();

    // room → entityId → entity
    private readonly Dictionary<string, Dictionary<int, ItemEntity>> _rooms
        = new Dictionary<string, Dictionary<int, ItemEntity>>();

    // playerNetId → hand state (-1 = empty)
    private readonly Dictionary<uint, PlayerHandState> _playerHands
        = new Dictionary<uint, PlayerHandState>();

    private int _nextEntityId = 1;

    private struct PlayerHandState
    {
        public int RightEntityId; // -1 = empty
        public int LeftEntityId;  // -1 = empty

        public static PlayerHandState Empty => new PlayerHandState { RightEntityId = -1, LeftEntityId = -1 };

        public bool IsHandFree(HandType hand) =>
            hand == HandType.Right ? RightEntityId == -1 : LeftEntityId == -1;

        public HandType? GetFreeHand()
        {
            if (RightEntityId == -1) return HandType.Right;
            if (LeftEntityId  == -1) return HandType.Left;
            return null;
        }

        public int GetEntityId(HandType hand) =>
            hand == HandType.Right ? RightEntityId : LeftEntityId;

        public void Set(HandType hand, int entityId)
        {
            if (hand == HandType.Right) RightEntityId = entityId;
            else                         LeftEntityId  = entityId;
        }

        public void Clear(HandType hand)
        {
            if (hand == HandType.Right) RightEntityId = -1;
            else                         LeftEntityId  = -1;
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Reset()
    {
        _rooms.Clear();
        _playerHands.Clear();
        _nextEntityId = 1;
        GameLogger.Network.Info("ServerItemManagerReset");
    }

    // ── Spawn / despawn ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a world item in the given room and broadcasts it to all players there.
    /// Returns the assigned entityId.
    /// </summary>
    public int SpawnItem(string roomId, int itemConfigId, Vector3 position, Quaternion rotation)
    {
        int entityId = _nextEntityId++;

        var entity = new ItemEntity
        {
            EntityId     = entityId,
            RoomId       = roomId,
            ItemConfigId = itemConfigId,
            Position     = position,
            Rotation     = rotation
        };

        if (!_rooms.TryGetValue(roomId, out var roomItems))
        {
            roomItems = new Dictionary<int, ItemEntity>();
            _rooms[roomId] = roomItems;
        }
        roomItems[entityId] = entity;

        GameLogger.Network.Info("Item Spawn entity={EntityId} configId={ItemConfigId} room={RoomId}",
            entityId, itemConfigId, roomId);

        BroadcastToRoom(roomId, new S2C_SpawnItem
        {
            EntityId     = entityId,
            RoomId       = roomId,
            ItemConfigId = itemConfigId,
            Position     = position,
            Rotation     = rotation,
            IsHeld       = false
        });

        return entityId;
    }

    /// <summary>
    /// Removes a world item from the room and tells all room clients to destroy it.
    /// Also drops the item from its holder's hand if currently held.
    /// </summary>
    public void DespawnItem(string roomId, int entityId)
    {
        if (!TryGetEntity(roomId, entityId, out var entity)) return;

        if (entity.IsHeld)
            ClearHolderHandState(entity);

        _rooms[roomId].Remove(entityId);

        GameLogger.Network.Info("Item Despawn entity={EntityId} room={RoomId}", entityId, roomId);
        BroadcastToRoom(roomId, new S2C_DestroyItem { EntityId = entityId, RoomId = roomId });
    }

    // ── C2S handlers ──────────────────────────────────────────────────────────

    public void HandlePickup(NetworkConnectionToClient conn, C2S_RequestPickupItem msg)
    {
        if (conn.identity == null) return;
        uint playerNetId = conn.identity.netId;
        string roomId    = PlayerRoomTracker.Instance.GetRoom(conn);

        GameLogger.Network.Debug("Item PickupRequest player={PlayerNetId} entity={EntityId} room={RoomId}",
            playerNetId, msg.EntityId, roomId);

        if (roomId == null)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Not in a room" });
            return;
        }

        if (!TryGetEntity(roomId, msg.EntityId, out var entity))
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Item not found" });
            return;
        }

        if (entity.IsHeld)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Already held" });
            return;
        }

        // Distance check
        Vector3 playerPos = conn.identity.transform.position;
        if (Vector3.Distance(playerPos, entity.Position) > 3f)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Too far" });
            return;
        }

        // Find a free hand
        var handState = GetOrCreateHandState(playerNetId);
        ItemConfig config = DatabaseManager.ItemConfigs.Find(x => x.ID == entity.ItemConfigId);

        HandType? assignedHand = ResolveHand(config, handState);
        if (!assignedHand.HasValue)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Hands full" });
            return;
        }

        // Attach
        entity.HolderNetId   = playerNetId;
        entity.HolderHand    = assignedHand.Value;
        entity.LocalPosition = Vector3.zero;
        entity.LocalRotation = Quaternion.identity;
        handState.Set(assignedHand.Value, msg.EntityId);
        _playerHands[playerNetId] = handState;

        GameLogger.Network.Info("Item Pickup player={PlayerNetId} entity={EntityId} hand={Hand} room={RoomId}",
            playerNetId, msg.EntityId, assignedHand.Value, roomId);

        conn.Send(new S2C_PickupResult { Success = true, EntityId = msg.EntityId });

        BroadcastToRoom(roomId, new S2C_ItemAttachedToHand
        {
            EntityId      = msg.EntityId,
            PlayerNetId   = playerNetId,
            HandType      = assignedHand.Value,
            LocalPosition = entity.LocalPosition,
            LocalRotation = entity.LocalRotation
        });
    }

    public void HandleDrop(NetworkConnectionToClient conn, C2S_RequestDropItem msg)
    {
        if (conn.identity == null) return;
        uint playerNetId = conn.identity.netId;
        string roomId    = PlayerRoomTracker.Instance.GetRoom(conn);

        GameLogger.Network.Debug("Item DropRequest player={PlayerNetId} hand={Hand}", playerNetId, msg.Hand);

        if (roomId == null)
        {
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = "Not in a room" });
            return;
        }

        if (!_playerHands.TryGetValue(playerNetId, out var handState))
        {
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = "No hand state" });
            return;
        }

        int entityId = handState.GetEntityId(msg.Hand);
        if (entityId == -1)
        {
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = "Hand empty" });
            return;
        }

        if (!TryGetEntity(roomId, entityId, out var entity))
        {
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = "Item not in room" });
            return;
        }

        // Drop position: slightly in front of player
        Vector3 dropPos = conn.identity.transform.position + conn.identity.transform.forward * 0.6f;
        if (Physics.Raycast(dropPos + Vector3.up, Vector3.down, out var hit, 5f, 1 << 9))
            dropPos = hit.point;

        entity.Position  = dropPos;
        entity.Rotation  = Quaternion.identity;
        ClearHolderHandState(entity);

        GameLogger.Network.Info("Item Drop player={PlayerNetId} entity={EntityId} hand={Hand} room={RoomId}",
            playerNetId, entityId, msg.Hand, roomId);

        conn.Send(new S2C_DropResult { Success = true, Hand = msg.Hand });

        BroadcastToRoom(roomId, new S2C_ItemDetachedFromHand
        {
            EntityId      = entityId,
            WorldPosition = dropPos,
            WorldRotation = Quaternion.identity
        });
    }

    public void HandleAdminSpawn(NetworkConnectionToClient conn, C2S_AdminSpawnItem msg)
    {
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null)
        {
            GameLogger.Network.Warning("AdminSpawn rejected: player not in a room conn={ConnId}", conn.connectionId);
            return;
        }

        int entityId = SpawnItem(roomId, msg.ItemConfigId, msg.Position, Quaternion.identity);
        GameLogger.Network.Info("Item AdminSpawn entity={EntityId} configId={ItemConfigId} room={RoomId} conn={ConnId}",
            entityId, msg.ItemConfigId, roomId, conn.connectionId);
    }

    public void HandleSwap(NetworkConnectionToClient conn, C2S_RequestSwapHands msg)
    {
        if (conn.identity == null) return;
        uint playerNetId = conn.identity.netId;
        string roomId    = PlayerRoomTracker.Instance.GetRoom(conn);

        if (roomId == null || !_playerHands.TryGetValue(playerNetId, out var handState)) return;

        int rightId = handState.RightEntityId;
        int leftId  = handState.LeftEntityId;

        if (rightId == -1 && leftId == -1) return;

        GameLogger.Network.Info("Item Swap player={PlayerNetId} right→left={R} left→right={L}",
            playerNetId, rightId, leftId);

        // Swap hand assignment in entities
        if (rightId != -1 && TryGetEntity(roomId, rightId, out var rightEntity))
            rightEntity.HolderHand = HandType.Left;
        if (leftId  != -1 && TryGetEntity(roomId, leftId,  out var leftEntity))
            leftEntity.HolderHand  = HandType.Right;

        (handState.RightEntityId, handState.LeftEntityId) = (leftId, rightId);
        _playerHands[playerNetId] = handState;

        // Notify all clients in room
        if (rightId != -1)
        {
            BroadcastToRoom(roomId, new S2C_ItemAttachedToHand
            {
                EntityId      = rightId,
                PlayerNetId   = playerNetId,
                HandType      = HandType.Left,
                LocalPosition = Vector3.zero,
                LocalRotation = Quaternion.identity
            });
        }
        if (leftId != -1)
        {
            BroadcastToRoom(roomId, new S2C_ItemAttachedToHand
            {
                EntityId      = leftId,
                PlayerNetId   = playerNetId,
                HandType      = HandType.Right,
                LocalPosition = Vector3.zero,
                LocalRotation = Quaternion.identity
            });
        }
    }

    // ── Room events ───────────────────────────────────────────────────────────

    public void OnPlayerEnterRoom(NetworkConnectionToClient conn, string roomId)
    {
        SendRoomSnapshot(conn, roomId);
    }

    public void OnPlayerLeaveRoom(NetworkConnectionToClient conn, string roomId)
    {
        if (conn.identity == null) return;
        DropAllHeldItems(conn.identity.netId, roomId);
    }

    public void OnPlayerDisconnect(NetworkConnectionToClient conn)
    {
        if (conn.identity == null) return;
        _playerHands.Remove(conn.identity.netId);
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    public void SendRoomSnapshot(NetworkConnectionToClient conn, string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var roomItems) || roomItems.Count == 0) return;

        GameLogger.Network.Debug("Item Snapshot room={RoomId} count={Count} conn={ConnId}",
            roomId, roomItems.Count, conn.connectionId);

        foreach (var entity in roomItems.Values)
        {
            conn.Send(new S2C_SpawnItem
            {
                EntityId      = entity.EntityId,
                RoomId        = entity.RoomId,
                ItemConfigId  = entity.ItemConfigId,
                Position      = entity.Position,
                Rotation      = entity.Rotation,
                IsHeld        = entity.IsHeld,
                HolderNetId   = entity.HolderNetId,
                HolderHand    = entity.HolderHand,
                LocalPosition = entity.LocalPosition,
                LocalRotation = entity.LocalRotation
            });
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    public ItemEntity GetEntity(string roomId, int entityId)
    {
        TryGetEntity(roomId, entityId, out var entity);
        return entity;
    }

    private bool TryGetEntity(string roomId, int entityId, out ItemEntity entity)
    {
        entity = null;
        return _rooms.TryGetValue(roomId, out var room) && room.TryGetValue(entityId, out entity);
    }

    private PlayerHandState GetOrCreateHandState(uint playerNetId)
    {
        if (!_playerHands.TryGetValue(playerNetId, out var state))
            state = PlayerHandState.Empty;
        return state;
    }

    private HandType? ResolveHand(ItemConfig config, PlayerHandState state)
    {
        if (config == null) return state.GetFreeHand();

        if (config.HandleType == ItemHandleType.TWO_HAND)
            return (state.RightEntityId == -1 && state.LeftEntityId == -1) ? HandType.Right : (HandType?)null;

        return state.GetFreeHand();
    }

    private void ClearHolderHandState(ItemEntity entity)
    {
        if (!entity.IsHeld) return;
        if (_playerHands.TryGetValue(entity.HolderNetId, out var state))
        {
            state.Clear(entity.HolderHand);
            _playerHands[entity.HolderNetId] = state;
        }
        entity.HolderNetId = 0;
    }

    private void DropAllHeldItems(uint playerNetId, string roomId)
    {
        if (!_playerHands.TryGetValue(playerNetId, out var state)) return;
        if (!_rooms.TryGetValue(roomId, out var roomItems)) return;

        foreach (var entity in roomItems.Values)
        {
            if (entity.HolderNetId != playerNetId) continue;

            Vector3 dropPos = entity.Position;
            ClearHolderHandState(entity);
            entity.Position = dropPos;

            BroadcastToRoom(roomId, new S2C_ItemDetachedFromHand
            {
                EntityId      = entity.EntityId,
                WorldPosition = dropPos,
                WorldRotation = Quaternion.identity
            });
        }

        _playerHands.Remove(playerNetId);
    }
    
    private static void BroadcastToRoom<T>(string roomId, T message) where T : struct, NetworkMessage {
        var connections = PlayerRoomTracker.Instance.GetConnectionsInRoom(roomId);
        int sentCount = 0;
        foreach (var conn in connections) {
            conn.Send(message);
            sentCount++;
        }
        if (sentCount > 0) {
            GameLogger.Network.Debug("BroadcastToRoom {RoomId} {MessageType} {RecipientCount}",
                roomId, typeof(T).Name, sentCount);
        }
    }
}
