using System.Collections;
using System.Collections.Generic;
using Mirror;
using Newtonsoft.Json;
using Sim;
using Sim.Entities.Persistence;
using Sim.Logging;
using UnityEngine;
using UnityEngine.Networking;

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

    // ── DB bridge ─────────────────────────────────────────────────────────────
    // Maps runtime entityId → DB item row. Mirrors PropDbBridge on ServerPropManager.
    // A bridge exists ONLY for items that have been persisted (rows in `items`
    // table). World items spawned on the map have no bridge and are purely
    // ephemeral — they vanish on server restart if not picked up.

    public class ItemDbBridge
    {
        public string Uuid;
        public int    Version;
        public string PlaceId;
    }

    private readonly Dictionary<int, ItemDbBridge> _bridges = new Dictionary<int, ItemDbBridge>();

    public ItemDbBridge GetBridge(int entityId) =>
        _bridges.TryGetValue(entityId, out var b) ? b : null;

    public void AssociateUuid(int entityId, string uuid, int version, string placeId) {
        _bridges[entityId] = new ItemDbBridge { Uuid = uuid, Version = version, PlaceId = placeId };
    }

    public void UpdateBridgeVersion(int entityId, int version) {
        if (_bridges.TryGetValue(entityId, out var b)) b.Version = version;
    }

    public void UpdateBridgePlace(int entityId, string placeId, int version) {
        if (_bridges.TryGetValue(entityId, out var b)) {
            b.PlaceId = placeId;
            b.Version = version;
        }
    }

    public void ClearBridge(int entityId) => _bridges.Remove(entityId);

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
        _bridges.Clear();
        _nextEntityId = 1;
        GameLogger.Network.Info("ServerItemManagerReset");
    }

    // ── Spawn / despawn ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates an item and attaches it directly to the player's free hand.
    /// Broadcasts S2C_SpawnItem with IsHeld=true — clients attach immediately, no world flash.
    /// Returns the entityId, or -1 if the player has no free hand.
    /// </summary>
    public int SpawnItemInHand(string roomId, int itemConfigId, NetworkConnectionToClient conn,
        ItemConfig config = null)
    {
        if (conn.identity == null) return -1;
        uint playerNetId = conn.identity.netId;

        var handState   = GetOrCreateHandState(playerNetId);
        var resolvedCfg = config ?? DatabaseManager.ItemConfigs.Find(x => x.ID == itemConfigId);
        HandType? hand  = ResolveHand(resolvedCfg, handState);

        if (!hand.HasValue)
            return -1;

        int entityId = _nextEntityId++;

        var entity = new ItemEntity
        {
            EntityId     = entityId,
            RoomId       = roomId,
            ItemConfigId = itemConfigId,
            Position     = conn.identity.transform.position,
            Rotation     = Quaternion.identity,
            HolderNetId  = playerNetId,
            HolderHand   = hand.Value,
            LocalPosition = Vector3.zero,
            LocalRotation = Quaternion.identity
        };

        if (!_rooms.TryGetValue(roomId, out var roomItems))
        {
            roomItems = new Dictionary<int, ItemEntity>();
            _rooms[roomId] = roomItems;
        }
        roomItems[entityId] = entity;

        handState.Set(hand.Value, entityId);
        _playerHands[playerNetId] = handState;

        GameLogger.Network.Info(
            "Item SpawnInHand entity={EntityId} configId={ItemConfigId} player={PlayerNetId} hand={Hand} room={RoomId}",
            entityId, itemConfigId, playerNetId, hand.Value, roomId);

        BroadcastToRoom(roomId, new S2C_SpawnItem
        {
            EntityId      = entityId,
            RoomId        = roomId,
            ItemConfigId  = itemConfigId,
            Position      = entity.Position,
            Rotation      = entity.Rotation,
            IsHeld        = true,
            HolderNetId   = playerNetId,
            HolderHand    = hand.Value,
            LocalPosition = Vector3.zero,
            LocalRotation = Quaternion.identity
        });

        if (entity.Persistent) PersistPickupAsync(conn, entityId, itemConfigId, hand.Value);

        return entityId;
    }



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

        // Supprime la ligne DB si elle existe (no-op pour les items éphémères).
        // Sans ça, un item consommé/admin-destroyed survivait au reconnect.
        PersistDropAsync(entityId);

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

        if (entity.AuthorizedNetId != 0 && entity.AuthorizedNetId != playerNetId)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Not yours" });
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

        if (entity.Persistent) PersistPickupAsync(conn, msg.EntityId, entity.ItemConfigId, assignedHand.Value);
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

        // DB: delete the persisted row — the item becomes an ephemeral world item
        // (no DB tracking). Fire-and-forget; the drop succeeds visually even if
        // the DELETE fails (orphan row in DB, to be cleaned later).
        if (entity.Persistent) PersistDropAsync(entityId);

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

        // DB: each held item changes hand_place. The right-hand item moves to
        // the left hand_place and vice-versa. Fire-and-forget — out-of-order
        // patches are safe because each PATCH carries an expectedVersion.
        PlayerInventory inv = conn.identity.GetComponent<PlayerInventory>();
        if (inv != null && inv.PlacesReady) {
            if (rightId != -1) PersistMoveToHandAsync(rightId, inv.HandLeftPlaceId);
            if (leftId  != -1) PersistMoveToHandAsync(leftId,  inv.HandRightPlaceId);
        }
    }

    // ── Room events ───────────────────────────────────────────────────────────

    public void OnPlayerEnterRoom(NetworkConnectionToClient conn, string roomId)
    {
        SendRoomSnapshot(conn, roomId);

        // Items in hand follow the player: every room entry rebuilds the
        // runtime entities from the DB hand places (idempotent — the previous
        // OnPlayerLeaveRoom cleaned up runtime state for this player).
        if (conn.identity == null) return;
        PlayerInventory inv = conn.identity.GetComponent<PlayerInventory>();
        if (inv == null || !inv.PlacesReady) return;     // EnsurePlaces should have completed before first room entry

        if (ApiManager.Instance != null) {
            ApiManager.Instance.StartCoroutine(RestoreHandItemsCoroutine(conn, roomId, inv));
        }
    }

    public void OnPlayerLeaveRoom(NetworkConnectionToClient conn, string roomId)
    {
        if (conn.identity == null) return;

        // Held items follow the player across rooms. On leave we just despawn
        // the runtime entities in the old room (clients there see them go) —
        // DB rows stay intact. The next OnPlayerEnterRoom (or RestoreHandItems
        // on reconnect) re-creates the entities in the new room. Same path
        // handles voluntary teleport AND disconnect (PlayerRoomTracker.OnDisconnect
        // calls LeaveRoom which fires this event).
        DespawnHeldItemsKeepingDb(conn.identity.netId, roomId);
    }

    public void OnPlayerDisconnect(NetworkConnectionToClient conn)
    {
        // Runtime cleanup is already handled by OnPlayerLeaveRoom (fired earlier
        // in the disconnect chain via PlayerRoomTracker.OnDisconnect). Nothing
        // extra to do here — DB rows for the player's held items persist and
        // will be restored on the next reconnect.
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

    /// <summary>
    /// Restreint le pickup à un seul joueur. 0 = aucune restriction (défaut).
    /// Utilisé par le système Jobs pour éviter le vol de colis mission.
    /// </summary>
    public void SetAuthorizedHolder(string roomId, int entityId, uint netId)
    {
        if (TryGetEntity(roomId, entityId, out var entity))
            entity.AuthorizedNetId = netId;
    }

    /// <summary>
    /// Active/désactive la persistance DB pour cet item. Si false, le pickup
    /// et le drop ne touchent pas l'API REST → l'item disparaît au disconnect
    /// (pas restauré au reconnect). Pour les items mission éphémères.
    /// </summary>
    public void SetPersistent(string roomId, int entityId, bool persistent)
    {
        if (TryGetEntity(roomId, entityId, out var entity))
            entity.Persistent = persistent;
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

    /// <summary>
    /// Vérifie si le joueur peut accueillir l'item dans une main.
    /// - ONE_HAND : au moins une main libre.
    /// - TWO_HAND : les DEUX mains doivent être libres.
    /// Utilisé par le flow d'achat avant de débiter le joueur.
    /// </summary>
    public bool CanFitInHand(uint playerNetId, ItemConfig config)
    {
        if (config == null) return false;
        var state = GetOrCreateHandState(playerNetId);
        return ResolveHand(config, state).HasValue;
    }

    /// <summary>
    /// Vrai si le joueur porte au moins un item éphémère (Persistent=false),
    /// typiquement un colis de mission. Utilisé pour bloquer certaines
    /// interactions (achat shop, etc.) tant que la mission est en cours.
    /// </summary>
    public bool IsHoldingEphemeralItem(uint playerNetId)
    {
        if (!_playerHands.TryGetValue(playerNetId, out var state)) return false;
        return HeldEntityIsEphemeral(state.LeftEntityId)
            || HeldEntityIsEphemeral(state.RightEntityId);
    }

    private bool HeldEntityIsEphemeral(int entityId)
    {
        if (entityId < 0) return false;
        foreach (var room in _rooms.Values)
        {
            if (room.TryGetValue(entityId, out var entity))
                return !entity.Persistent;
        }
        return false;
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

    /// <summary>
    /// Called when a player leaves a room (teleport OR disconnect). The
    /// player's held runtime entities are removed from the room dict and a
    /// S2C_DestroyItem is broadcast to remaining room members so they stop
    /// rendering them. DB rows are PRESERVED — the next OnPlayerEnterRoom (or
    /// reconnect) re-creates the entities in the new room via
    /// RestoreHandItemsCoroutine. Bridges of the destroyed entityIds are
    /// cleared (entityIds aren't reused; the new entities get fresh bridges).
    /// </summary>
    private void DespawnHeldItemsKeepingDb(uint playerNetId, string roomId) {
        if (!_rooms.TryGetValue(roomId, out var roomItems)) return;

        List<int> toDestroy = new List<int>();
        foreach (var entity in roomItems.Values) {
            if (entity.HolderNetId == playerNetId) toDestroy.Add(entity.EntityId);
        }
        foreach (int entityId in toDestroy) {
            roomItems.Remove(entityId);
            ClearBridge(entityId);
            BroadcastToRoom(roomId, new S2C_DestroyItem { EntityId = entityId, RoomId = roomId });
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

    // ── DB persistence (fire-and-forget coroutines hosted by ApiManager) ─────

    private void PersistPickupAsync(NetworkConnectionToClient conn, int entityId, int itemConfigId, HandType hand) {
        if (conn?.identity == null || ApiManager.Instance == null) return;
        PlayerInventory inv = conn.identity.GetComponent<PlayerInventory>();
        if (inv == null || !inv.PlacesReady) {
            Debug.LogWarning($"[ServerItemManager] PersistPickup skipped — PlayerInventory not ready (entity={entityId})");
            return;
        }
        PlayerController player = conn.identity.GetComponent<PlayerController>();
        string charId = player?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) {
            Debug.LogWarning($"[ServerItemManager] PersistPickup skipped — character id missing (entity={entityId})");
            return;
        }
        string handPlaceId = inv.HandPlaceFor(hand);
        if (string.IsNullOrEmpty(handPlaceId)) return;

        ApiManager.Instance.StartCoroutine(UpsertItemCoroutine(entityId, itemConfigId, handPlaceId, charId));
    }

    private IEnumerator UpsertItemCoroutine(int entityId, int itemConfigId, string handPlaceId, string charId) {
        UpsertItemBody body = new UpsertItemBody {
            placeId  = handPlaceId,
            configId = itemConfigId,
            quantity = 1,
            ownedBy  = charId,
        };
        UnityWebRequest req = ApiManager.Instance.UpsertItemRequest(body);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] Upsert item failed code={req.responseCode} body={req.downloadHandler?.text}");
            yield break;
        }

        try {
            ItemJson item = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text);
            if (item != null && !string.IsNullOrEmpty(item.Id)) {
                AssociateUuid(entityId, item.Id, item.version, handPlaceId);
            }
        } catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] Upsert response parse error: {e.Message}");
        }
    }

    private void PersistDropAsync(int entityId) {
        if (ApiManager.Instance == null) return;
        ItemDbBridge bridge = GetBridge(entityId);
        if (bridge == null) return;     // ephemeral item, nothing to delete
        ApiManager.Instance.StartCoroutine(DeleteItemCoroutine(entityId, bridge.Uuid));
    }

    private IEnumerator DeleteItemCoroutine(int entityId, string itemUuid) {
        UnityWebRequest req = ApiManager.Instance.DeleteItemRequest(itemUuid);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] Delete item {itemUuid} failed code={req.responseCode} body={req.downloadHandler?.text}");
            // Keep the bridge so a retry could be attempted later — though we have no retry path today.
            yield break;
        }

        ClearBridge(entityId);
    }

    private void PersistMoveToHandAsync(int entityId, string newHandPlaceId) {
        if (ApiManager.Instance == null) return;
        ItemDbBridge bridge = GetBridge(entityId);
        if (bridge == null || bridge.PlaceId == newHandPlaceId) return;
        ApiManager.Instance.StartCoroutine(MoveItemCoroutine(entityId, bridge, newHandPlaceId));
    }

    private IEnumerator MoveItemCoroutine(int entityId, ItemDbBridge bridge, string newPlaceId) {
        UpdateItemBody body = new UpdateItemBody {
            expectedVersion = bridge.Version,
            placeId         = newPlaceId,
        };
        UnityWebRequest req = ApiManager.Instance.UpdateItemRequest(bridge.Uuid, body);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] Move item {bridge.Uuid} → {newPlaceId} failed code={req.responseCode} body={req.downloadHandler?.text}");
            yield break;
        }

        try {
            ItemJson item = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text);
            if (item != null) UpdateBridgePlace(entityId, newPlaceId, item.version);
        } catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] Move item response parse error: {e.Message}");
        }
    }

    /// <summary>
    /// On first room entry after connect, fetch the player's hand_left and
    /// hand_right place state and re-create runtime entities for each item
    /// found, attached to the corresponding hand. Items dropped on map during
    /// previous session are NOT restored (they were ephemeral).
    /// </summary>
    private IEnumerator RestoreHandItemsCoroutine(NetworkConnectionToClient conn, string roomId, PlayerInventory inv) {
        if (conn?.identity == null) yield break;
        uint playerNetId = conn.identity.netId;

        yield return RestoreOneHand(conn, roomId, playerNetId, inv.HandLeftPlaceId,  HandType.Left);
        yield return RestoreOneHand(conn, roomId, playerNetId, inv.HandRightPlaceId, HandType.Right);
    }

    private IEnumerator RestoreOneHand(NetworkConnectionToClient conn, string roomId, uint playerNetId, string handPlaceId, HandType hand) {
        if (string.IsNullOrEmpty(handPlaceId)) yield break;

        UnityWebRequest req = ApiManager.Instance.GetPlaceStateRequest(handPlaceId);
        yield return req.SendWebRequest();

        if (req.responseCode != 200) {
            Debug.LogWarning($"[ServerItemManager] RestoreHand({hand}) GET /places/{handPlaceId}/state failed code={req.responseCode}");
            yield break;
        }

        PlaceStateJson state;
        try {
            state = JsonConvert.DeserializeObject<PlaceStateJson>(req.downloadHandler.text);
        } catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] RestoreHand({hand}) parse error: {e.Message}");
            yield break;
        }

        if (state?.items == null) yield break;

        foreach (ItemJson it in state.items) {
            // Re-create the runtime entity attached to the player in the current room.
            int entityId = _nextEntityId++;
            var entity = new ItemEntity {
                EntityId      = entityId,
                RoomId        = roomId,
                ItemConfigId  = it.configId,
                Position      = conn.identity.transform.position,
                Rotation      = Quaternion.identity,
                HolderNetId   = playerNetId,
                HolderHand    = hand,
                LocalPosition = Vector3.zero,
                LocalRotation = Quaternion.identity,
            };

            if (!_rooms.TryGetValue(roomId, out var roomItems)) {
                roomItems = new Dictionary<int, ItemEntity>();
                _rooms[roomId] = roomItems;
            }
            roomItems[entityId] = entity;

            var handState = GetOrCreateHandState(playerNetId);
            handState.Set(hand, entityId);
            _playerHands[playerNetId] = handState;

            AssociateUuid(entityId, it.Id, it.version, handPlaceId);

            BroadcastToRoom(roomId, new S2C_SpawnItem {
                EntityId      = entityId,
                RoomId        = roomId,
                ItemConfigId  = it.configId,
                Position      = entity.Position,
                Rotation      = entity.Rotation,
                IsHeld        = true,
                HolderNetId   = playerNetId,
                HolderHand    = hand,
                LocalPosition = Vector3.zero,
                LocalRotation = Quaternion.identity,
            });

            GameLogger.Network.Info("Item Restore entity={EntityId} configId={ItemConfigId} player={PlayerNetId} hand={Hand}",
                entityId, it.configId, playerNetId, hand);
        }
    }
}
