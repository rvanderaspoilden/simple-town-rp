using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Newtonsoft.Json;
using Sim;
using Sim.Entities.Persistence;
using Sim.Logging;
using Sim.Scriptables;
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

    // Items éphémères (Persistent=false, donc sans ligne DB) tenus en main au moment
    // où le joueur quitte une room. Stockés ici pour être re-spawnés dans la nouvelle
    // room à l'entrée — sinon ils disparaîtraient (RestoreHandItems ne restaure que
    // les items persistés en DB). Ex : sac poubelle (config 101), colis de mission.
    private struct CarriedEphemeralItem {
        public int      ItemConfigId;
        public HandType Hand;
        public uint     AuthorizedNetId;
    }
    private readonly Dictionary<uint, List<CarriedEphemeralItem>> _carriedEphemeral
        = new Dictionary<uint, List<CarriedEphemeralItem>>();

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

    // ── Container sessions ────────────────────────────────────────────────────
    // Quand un joueur ouvre un conteneur, on alloue des entityId éphémères pour
    // les items qu'il contient (lus depuis la DB via /places/:id/state). Une
    // session = un joueur ↔ un conteneur ouvert à la fois (MVP). Les bridges
    // entityId→UUID sont stockés dans `_bridges` (existant) pour partager le
    // même pipeline de PATCH item que les mains.

    private sealed class ContainerSessionItem {
        public int    EntityId;
        public int    ConfigId;
        public int    SlotIndex;
        public string ItemUuid;
        public int    Version;
    }

    private sealed class ContainerSession {
        public int    PropId;
        public string PlaceId;
        public string PropUuid;
        public string RoomId;       // Room du prop au moment de l'ouverture : utilisée pour
                                    // broadcaster le S2C_ContainerVisualState à la fermeture
                                    // même quand le joueur a déjà quitté la room (disconnect).
        public uint   OpenedBy;
        public ContainerConfig Config;
        public Dictionary<int, ContainerSessionItem> ItemsByEntityId = new Dictionary<int, ContainerSessionItem>();
        public Dictionary<int, int> SlotToEntityId = new Dictionary<int, int>();
    }

    private readonly Dictionary<uint, ContainerSession> _openContainerByPlayer
        = new Dictionary<uint, ContainerSession>();

    // ── Pocket sessions (toujours ouvertes pour la durée de connexion) ────────
    // Chaque joueur a UNE poche persistée à la place "pocket:{charId}", 2 slots
    // MVP. La session est créée par EnsurePocketSession au connect et libérée
    // à la déconnexion.
    private const int PocketCapacity = 2;

    private sealed class PocketSessionItem {
        public int    EntityId;
        public int    ConfigId;
        public int    SlotIndex;
        public string ItemUuid;
        public int    Version;
    }

    private sealed class PocketSession {
        public string PlaceId;      // UUID backend
        public Dictionary<int, PocketSessionItem> ItemsByEntityId = new Dictionary<int, PocketSessionItem>();
        public Dictionary<int, int> SlotToEntityId = new Dictionary<int, int>();
    }

    private readonly Dictionary<uint, PocketSession> _pocketByPlayer
        = new Dictionary<uint, PocketSession>();

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
        _carriedEphemeral.Clear();
        _openContainerByPlayer.Clear();
        _pocketByPlayer.Clear();
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
        ItemConfig config = null, bool persistent = true)
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
            LocalRotation = Quaternion.identity,
            Persistent    = persistent
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

    // ── Persistent world items ──────────────────────────────────────────────────
    // Unlike ordinary world items (ephemeral), a persistent world item lies on the
    // ground AND is backed by a DB row carrying its world position, so it survives a
    // server restart. It is removed from the DB the moment a player collects it (see
    // HandlePickup). Eligibility is driven by ItemConfig.ToPersist; gameplay concepts
    // (e.g. "Déchet" for the cleaner job) sit on top and are decoupled from this layer.

    /// <summary>
    /// Spawns a persistent world item: a normal world entity PLUS a DB row in
    /// <paramref name="placeId"/> carrying its position/rotation. Persistence requires
    /// BOTH a placeId AND a config flagged ToPersist — otherwise it falls back to a
    /// plain ephemeral world item.
    /// </summary>
    public int SpawnPersistentWorldItem(string roomId, string placeId, int itemConfigId,
        Vector3 position, Quaternion rotation, string ownerCharId)
    {
        int entityId = SpawnItem(roomId, itemConfigId, position, rotation);

        ItemConfig config = DatabaseManager.ItemConfigs.Find(x => x.ID == itemConfigId);
        if (config == null || !config.ToPersist) {
            Debug.LogWarning($"[ServerItemManager] SpawnPersistentWorldItem: config {itemConfigId} is not ToPersist — spawning ephemeral");
            return entityId;
        }

        if (!string.IsNullOrEmpty(placeId) && ApiManager.Instance != null)
            ApiManager.Instance.StartCoroutine(
                CreateWorldItemCoroutine(entityId, placeId, itemConfigId, position, rotation, ownerCharId));

        return entityId;
    }

    private IEnumerator CreateWorldItemCoroutine(int entityId, string placeId, int itemConfigId,
        Vector3 position, Quaternion rotation, string ownerCharId)
    {
        CreateItemBody body = new CreateItemBody {
            placeId  = placeId,
            configId = itemConfigId,
            quantity = 1,
            ownedBy  = string.IsNullOrEmpty(ownerCharId) ? null : ownerCharId,
            position = new Vector3Body(position),
            rotation = new Vector3Body(rotation.eulerAngles),
        };

        UnityWebRequest req = ApiManager.Instance.CreateItemRequest(body);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] Create waste item failed code={req.responseCode} body={req.downloadHandler?.text}");
            yield break;
        }

        try {
            ItemJson item = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text);
            if (item != null && !string.IsNullOrEmpty(item.Id))
                AssociateUuid(entityId, item.Id, item.version, placeId);
        } catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] Create waste response parse error: {e.Message}");
        }
    }

    /// <summary>
    /// Re-spawns a persisted world item loaded from the DB (existing UUID/version),
    /// re-bridging it so a later pickup deletes the right row. No POST — the row already exists.
    /// </summary>
    public int SpawnPersistentWorldFromDb(string roomId, int itemConfigId, Vector3 position, Quaternion rotation,
        string uuid, int version, string placeId)
    {
        int entityId = SpawnItem(roomId, itemConfigId, position, rotation);
        if (!string.IsNullOrEmpty(uuid))
            AssociateUuid(entityId, uuid, version, placeId);
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

        // A bridged world item — e.g. a Déchet (Waste) collected from the ground — is
        // removed from the DB on pickup so it never re-spawns; it becomes an ephemeral
        // held item. Other items persist into the player's hand place as before.
        if (GetBridge(msg.EntityId) != null)
            PersistDropAsync(msg.EntityId);
        else if (entity.Persistent)
            PersistPickupAsync(conn, msg.EntityId, entity.ItemConfigId, assignedHand.Value);
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

        // Always run the restore coroutine: it rebuilds persisted hand items from DB
        // (when places are ready) AND re-spawns ephemeral items carried over from the
        // previous room (which have no DB row).
        if (ApiManager.Instance != null) {
            ApiManager.Instance.StartCoroutine(RestoreHandItemsCoroutine(conn, roomId, inv));
        }

        // Pocket : c'est ICI qu'on hydrate la session (conn.identity est garanti set
        // après AddPlayerForConnection). Si elle existe déjà, on re-push juste le
        // snapshot pour rattraper un client dont l'UI n'était pas encore abonnée.
        // Sinon, on lance EnsurePocketSession qui fetch la DB et populera la session.
        if (_pocketByPlayer.TryGetValue(conn.identity.netId, out var pocketSession)) {
            // Cas A : session déjà hydratée (ex. re-entrée après un room change, ou
            // PocketPlaceContext.Install a créé une session à la volée pendant un move).
            // Si elle est vide ET qu'on n'a jamais fetch la DB, on refait l'hydratation.
            if (pocketSession.ItemsByEntityId.Count == 0 && ApiManager.Instance != null) {
                ApiManager.Instance.StartCoroutine(EnsurePocketSession(conn));
            } else {
                SendPocketSnapshot(conn, pocketSession);
            }
        } else if (ApiManager.Instance != null) {
            ApiManager.Instance.StartCoroutine(EnsurePocketSession(conn));
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
        // in the disconnect chain via PlayerRoomTracker.OnDisconnect). DB rows for
        // the player's held items persist and will be restored on the next reconnect.
        // Discard any carried-over ephemeral items — they are not persisted and the
        // player isn't going to re-enter a room this session.
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        _carriedEphemeral.Remove(netId);

        // Toute session conteneur en cours est fermée (libère les bridges éphémères).
        CloseSession(netId);
        // Idem pour la session poche : libère les entityId+bridges alloués au connect.
        CleanupPocketSession(netId);

        // Filet de sécurité : despawn TOUT item WORLD encore en scène dont le verrou
        // propriétaire correspond à ce joueur (AuthorizedNetId == netId). Cas typique :
        // colis de Sort spawnés au pickup et non encore ramassés au moment du crash.
        // En temps normal, SortItemsStepInstance.OnExit s'en charge via la chaîne
        // JobServerManager.OnPlayerDisconnected → FinalizeFail → step.OnExit ;
        // ce filet couvre les races où cette chaîne ne parvient pas à boucler.
        DespawnOrphanWorldItemsAuthorizedBy(netId);
    }

    /// <summary>
    /// Despawn tous les items WORLD (non tenus) à travers toutes les rooms dont
    /// le verrou <see cref="ItemEntity.AuthorizedNetId"/> correspond à <paramref name="netId"/>.
    /// Idempotent : si la chaîne JobServerManager a déjà nettoyé un item, le
    /// DespawnItem retombe sur TryGetEntity false et part en no-op.
    /// </summary>
    private void DespawnOrphanWorldItemsAuthorizedBy(uint netId)
    {
        if (netId == 0u || _rooms.Count == 0) return;
        // Snapshot des (roomId, entityId) avant de modifier les dicos.
        List<(string roomId, int entityId)> orphans = null;
        foreach (var kv in _rooms)
        {
            foreach (var entity in kv.Value.Values)
            {
                if (entity.IsHeld) continue;
                if (entity.AuthorizedNetId != netId) continue;
                (orphans ??= new List<(string, int)>()).Add((kv.Key, entity.EntityId));
            }
        }
        if (orphans == null) return;
        foreach (var (roomId, entityId) in orphans)
        {
            DespawnItem(roomId, entityId);
        }
        GameLogger.Network.Info("OrphanMissionItems Despawn netId={NetId} count={Count}",
            netId, orphans.Count);
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
    /// Vrai si le joueur porte au moins un item de **mission** en main. Le critère est
    /// le verrou propriétaire (AuthorizedNetId != 0), posé uniquement par les steps de
    /// mission (PickupPackage / SortItems / UseMachine via SetAuthorizedHolder) — PAS par
    /// les items éphémères ordinaires comme le sac poubelle. Utilisé pour bloquer certaines
    /// interactions (achat shop, etc.) tant que la mission est en cours.
    /// </summary>
    public bool IsHoldingMissionItem(uint playerNetId)
    {
        if (!_playerHands.TryGetValue(playerNetId, out var state)) return false;
        return HeldEntityIsMissionLocked(state.LeftEntityId)
            || HeldEntityIsMissionLocked(state.RightEntityId);
    }

    private bool HeldEntityIsMissionLocked(int entityId)
    {
        if (entityId < 0) return false;
        foreach (var room in _rooms.Values)
        {
            if (room.TryGetValue(entityId, out var entity))
                return entity.AuthorizedNetId != 0;
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
        List<CarriedEphemeralItem> carried = null;
        foreach (var entity in roomItems.Values) {
            if (entity.HolderNetId != playerNetId) continue;
            toDestroy.Add(entity.EntityId);

            // Item éphémère (pas de ligne DB) → on mémorise sa config/main pour le
            // re-spawner dans la nouvelle room (sinon il serait perdu).
            if (!entity.Persistent) {
                carried ??= new List<CarriedEphemeralItem>();
                carried.Add(new CarriedEphemeralItem {
                    ItemConfigId    = entity.ItemConfigId,
                    Hand            = entity.HolderHand,
                    AuthorizedNetId = entity.AuthorizedNetId,
                });
            }
        }
        foreach (int entityId in toDestroy) {
            roomItems.Remove(entityId);
            ClearBridge(entityId);
            BroadcastToRoom(roomId, new S2C_DestroyItem { EntityId = entityId, RoomId = roomId });
        }

        if (carried != null) _carriedEphemeral[playerNetId] = carried;
        else                 _carriedEphemeral.Remove(playerNetId);

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

        // Idempotence : retire d'abord les items runtime déjà tenus par ce joueur dans
        // cette room avant de re-restaurer. Sans ça, un OnPlayerEnterRoom rejoué pour la
        // même room (ou un chevauchement) recréait un 2e exemplaire tenu : "manger"
        // n'en supprimait qu'un et l'autre restait visible jusqu'au changement de room.
        DespawnHeldRuntimeForRestore(playerNetId, roomId);

        // Restore persisted hand items from DB (only when the player's hand places exist).
        if (inv != null && inv.PlacesReady) {
            yield return RestoreOneHand(conn, roomId, playerNetId, inv.HandLeftPlaceId,  HandType.Left);
            yield return RestoreOneHand(conn, roomId, playerNetId, inv.HandRightPlaceId, HandType.Right);
        }

        // Then re-spawn ephemeral items the player carried over from the previous room
        // (no DB row). Done after the DB restore so hand slots are reconciled first.
        RespawnCarriedEphemeral(conn, roomId);
    }

    /// <summary>
    /// Destroys the player's currently-held runtime entities in the given room WITHOUT
    /// touching DB rows or the carried-ephemeral stash. Makes hand restore idempotent so
    /// a repeated/overlapping room-enter can't leave duplicate held entities (one copy
    /// would survive "consume" and only vanish on the next room change).
    /// </summary>
    private void DespawnHeldRuntimeForRestore(uint playerNetId, string roomId) {
        if (!_rooms.TryGetValue(roomId, out var roomItems)) return;

        List<int> toDestroy = new List<int>();
        foreach (var entity in roomItems.Values)
            if (entity.HolderNetId == playerNetId) toDestroy.Add(entity.EntityId);

        foreach (int entityId in toDestroy) {
            roomItems.Remove(entityId);
            ClearBridge(entityId);
            BroadcastToRoom(roomId, new S2C_DestroyItem { EntityId = entityId, RoomId = roomId });
        }

        if (toDestroy.Count > 0) {
            _playerHands.Remove(playerNetId);
            GameLogger.Network.Debug("Item RestoreDedupe player={PlayerNetId} room={RoomId} removed={Count}",
                playerNetId, roomId, toDestroy.Count);
        }
    }

    /// <summary>
    /// Re-spawns the player's carried-over ephemeral items (e.g. trash bag, mission
    /// package) in the room they just entered, attached to the same hand when possible.
    /// These have no DB row, so they wouldn't be restored by RestoreOneHand.
    /// </summary>
    private void RespawnCarriedEphemeral(NetworkConnectionToClient conn, string roomId) {
        if (conn?.identity == null) return;
        uint playerNetId = conn.identity.netId;

        if (!_carriedEphemeral.TryGetValue(playerNetId, out var carried)) return;
        _carriedEphemeral.Remove(playerNetId);

        foreach (var c in carried) {
            var handState = GetOrCreateHandState(playerNetId);

            // Prefer the original hand; fall back to any free hand if it's now taken
            // (e.g. a persisted item was just restored into it).
            HandType hand = c.Hand;
            if (!handState.IsHandFree(hand)) {
                HandType? free = handState.GetFreeHand();
                if (!free.HasValue) {
                    GameLogger.Network.Warning("Item CarryEphemeral dropped (no free hand) configId={ItemConfigId} player={PlayerNetId}",
                        c.ItemConfigId, playerNetId);
                    continue;
                }
                hand = free.Value;
            }

            int entityId = _nextEntityId++;
            var entity = new ItemEntity {
                EntityId        = entityId,
                RoomId          = roomId,
                ItemConfigId    = c.ItemConfigId,
                Position        = conn.identity.transform.position,
                Rotation        = Quaternion.identity,
                HolderNetId     = playerNetId,
                HolderHand      = hand,
                LocalPosition   = Vector3.zero,
                LocalRotation   = Quaternion.identity,
                Persistent      = false,
                AuthorizedNetId = c.AuthorizedNetId,
            };

            if (!_rooms.TryGetValue(roomId, out var roomItems)) {
                roomItems = new Dictionary<int, ItemEntity>();
                _rooms[roomId] = roomItems;
            }
            roomItems[entityId] = entity;

            handState.Set(hand, entityId);
            _playerHands[playerNetId] = handState;

            BroadcastToRoom(roomId, new S2C_SpawnItem {
                EntityId      = entityId,
                RoomId        = roomId,
                ItemConfigId  = c.ItemConfigId,
                Position      = entity.Position,
                Rotation      = entity.Rotation,
                IsHeld        = true,
                HolderNetId   = playerNetId,
                HolderHand    = hand,
                LocalPosition = Vector3.zero,
                LocalRotation = Quaternion.identity,
            });

            GameLogger.Network.Info("Item CarryEphemeral entity={EntityId} configId={ItemConfigId} player={PlayerNetId} hand={Hand} room={RoomId}",
                entityId, c.ItemConfigId, playerNetId, hand, roomId);
        }
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

    // ── Container handlers ────────────────────────────────────────────────────

    // Garde l'enum pour le typage de IPlaceContext.Kind (lecture côté code orchestrateur).
    // La classification placeId → PlaceKind et la résolution placeKey → UUID backend sont
    // désormais portées par ResolvePlace + les IPlaceContext concrets.
    private enum PlaceKind { Unknown, HandLeft, HandRight, Container, Pocket }

    private static int ReadSlotIndex(Dictionary<string, object> stateData) {
        if (stateData == null || !stateData.TryGetValue("slotIndex", out var v) || v == null) return 0;
        if (v is long l) return (int)l;
        if (v is int i)  return i;
        if (v is double d) return (int)d;
        return int.TryParse(v.ToString(), out var p) ? p : 0;
    }

    public void HandleOpenContainer(NetworkConnectionToClient conn, C2S_OpenContainer msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;

        // Un seul conteneur ouvert à la fois par joueur (MVP).
        CloseSession(netId);

        var go = ServerPropManager.Instance.GetSpawnedGameObject(msg.PropId);
        if (go == null) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = msg.PropId, ErrorMessage = "Prop introuvable" });
            return;
        }
        var behaviour = go.GetComponent<PropBehaviourBase>();
        var config = behaviour?.GetConfiguration();
        if (config == null || config.Container == null || !config.Container.IsContainer) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = msg.PropId, ErrorMessage = "Pas un conteneur" });
            return;
        }

        if (Vector3.Distance(conn.identity.transform.position, go.transform.position)
            > config.GetRangeToInteract() + 1.5f) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = msg.PropId, ErrorMessage = "Trop loin" });
            return;
        }

        string propUuid = ServerPropManager.Instance.GetUuid(msg.PropId);
        if (string.IsNullOrEmpty(propUuid)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = msg.PropId, ErrorMessage = "Prop non persisté (UUID manquant)" });
            return;
        }

        var player = conn.identity.GetComponent<Sim.PlayerController>();
        string charId = player?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = msg.PropId, ErrorMessage = "Sans caractère" });
            return;
        }

        ApiManager.Instance?.StartCoroutine(
            OpenContainerCoroutine(conn, netId, msg.PropId, propUuid, charId, config.Container));
    }

    private IEnumerator OpenContainerCoroutine(NetworkConnectionToClient conn, uint netId, int propId,
        string propUuid, string charId, ContainerConfig containerCfg)
    {
        // 1. Place idempotent (POST /places).
        string placeKey = $"container:{propUuid}";
        var createBody = new CreatePlaceBody {
            placeKey = placeKey,
            type = "container",
            ownerId = charId,
            properties = new Dictionary<string, object> {
                { "slotCount",     containerCfg.SlotCount },
                { "acceptedTypes", containerCfg.AcceptedTypes.Select(t => (int)t).ToArray() },
            },
        };
        UnityWebRequest createReq = ApiManager.Instance.CreatePlaceRequest(createBody);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode < 200 || createReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] OpenContainer create place failed code={createReq.responseCode}");
            conn.Send(new S2C_ContainerOpenFailed { PropId = propId, ErrorMessage = "Erreur place" });
            yield break;
        }
        PlaceJson placeData = null;
        try { placeData = JsonConvert.DeserializeObject<PlaceJson>(createReq.downloadHandler.text); }
        catch (System.Exception e) { Debug.LogWarning($"[ServerItemManager] OpenContainer parse: {e.Message}"); }
        string placeId = placeData?.Id;
        if (string.IsNullOrEmpty(placeId)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = propId, ErrorMessage = "Place sans id" });
            yield break;
        }

        // 2. State fetch.
        UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
        yield return stateReq.SendWebRequest();
        if (stateReq.responseCode != 200) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = propId, ErrorMessage = "Erreur fetch state" });
            yield break;
        }
        PlaceStateJson stateData = null;
        try { stateData = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text); }
        catch (System.Exception e) { Debug.LogWarning($"[ServerItemManager] OpenContainer state parse: {e.Message}"); }

        // 3. Alloue entityId session + bridges + snapshot.
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        var session = new ContainerSession {
            PropId = propId, PlaceId = placeId, PropUuid = propUuid,
            RoomId = roomId,
            OpenedBy = netId, Config = containerCfg,
        };
        var snapshot = new List<S2C_ContainerItem>();
        if (stateData?.items != null) {
            foreach (var it in stateData.items) {
                int slotIndex = ReadSlotIndex(it.stateData);
                int entityId = _nextEntityId++;
                session.ItemsByEntityId[entityId] = new ContainerSessionItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex,
                    ItemUuid = it.Id, Version = it.version,
                };
                if (!session.SlotToEntityId.ContainsKey(slotIndex)) session.SlotToEntityId[slotIndex] = entityId;
                snapshot.Add(new S2C_ContainerItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex,
                });
                _bridges[entityId] = new ItemDbBridge {
                    Uuid = it.Id, Version = it.version, PlaceId = placeId,
                };
            }
        }
        _openContainerByPlayer[netId] = session;

        conn.Send(new S2C_ContainerOpened {
            PropId = propId,
            PlaceId = placeId,
            SlotCount = containerCfg.SlotCount,
            AcceptedTypes = containerCfg.AcceptedTypes.Select(t => (byte)t).ToArray(),
            Items = snapshot.ToArray(),
        });

        // Broadcast visuel : porte/couvercle s'ouvre pour tout le monde dans la room.
        if (!string.IsNullOrEmpty(roomId)) {
            BroadcastToRoom(roomId, new S2C_ContainerVisualState {
                RoomId = roomId, PropId = propId, IsOpen = true,
            });
        }

        GameLogger.Network.Info("ContainerOpened propId={PropId} placeId={PlaceId} items={Count} netId={NetId}",
            propId, placeId, snapshot.Count, netId);
    }

    public void HandleCloseContainer(NetworkConnectionToClient conn, C2S_CloseContainer msg) {
        if (conn?.identity == null) return;
        CloseSession(conn.identity.netId);
    }

    private void CloseSession(uint netId) {
        if (!_openContainerByPlayer.TryGetValue(netId, out var session)) return;
        _openContainerByPlayer.Remove(netId);
        // Libère les bridges session (les entityId allouent à la session ne sont
        // plus valides après close ; un re-open re-allouera des nouveaux ids).
        foreach (var it in session.ItemsByEntityId.Values) _bridges.Remove(it.EntityId);

        // Broadcast visuel : porte/couvercle se referme. session.RoomId est figé
        // au moment de l'open → fonctionne même si OpenedBy a quitté la room/déco.
        if (!string.IsNullOrEmpty(session.RoomId)) {
            BroadcastToRoom(session.RoomId, new S2C_ContainerVisualState {
                RoomId = session.RoomId, PropId = session.PropId, IsOpen = false,
            });
        }

        GameLogger.Network.Debug("ContainerClosed netId={NetId} placeId={PlaceId}", netId, session.PlaceId);
    }

    public void HandleMoveItem(NetworkConnectionToClient conn, C2S_MoveItem msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        var player = conn.identity.GetComponent<Sim.PlayerController>();
        string charId = player?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Sans caractère" });
            return;
        }

        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        var fromCtx = ResolvePlace(conn, netId, charId, msg.FromPlaceId, roomId);
        var toCtx   = ResolvePlace(conn, netId, charId, msg.ToPlaceId,   roomId);
        if (fromCtx == null || toCtx == null) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Place inconnue" });
            return;
        }

        if (!fromCtx.TryResolveItem(msg.EntityId, declaredSlot: -1, out var itemCtx, out var err)
            || !toCtx.ValidateAsTarget(msg.ToSlotIndex, itemCtx.ConfigId, out err)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = err });
            // Push les snapshots pour reconcilier le client : il a déjà fait le visual
            // move dans OnDrop, sans snapshot il garderait un draggable fantôme dans le
            // slot de destination — qui répondrait ensuite "introuvable" à toute tentative.
            PushSnapshotsFor(conn, fromCtx, toCtx);
            return;
        }
        if (!toCtx.IsSlotAvailableFor(msg.ToSlotIndex, msg.EntityId)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Slot occupé" });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            return;
        }

        ApiManager.Instance?.StartCoroutine(MoveItemCoroutine(conn, msg, itemCtx, fromCtx, toCtx));
    }

    private IEnumerator MoveItemCoroutine(NetworkConnectionToClient conn, C2S_MoveItem msg,
        ItemCtx itemCtx, IPlaceContext fromCtx, IPlaceContext toCtx)
    {
        if (string.IsNullOrEmpty(toCtx.PlaceId)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Place backend non résolue" });
            yield break;
        }

        // Item éphémère (no DB row → no UUID) : autorisé pour hand↔hand uniquement,
        // car pocket/container ont une autorité persistante. On le filtre ici plutôt
        // que dans ValidateAsTarget pour garder l'interface du context simple.
        bool isEphemeral = string.IsNullOrEmpty(itemCtx.ItemUuid);
        if (isEphemeral && toCtx.Kind != PlaceKind.HandLeft && toCtx.Kind != PlaceKind.HandRight) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId,
                ErrorMessage = InventoryToasts.NotStorable });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            yield break;
        }

        int newVersion = itemCtx.Version + 1;
        if (!isEphemeral) {
            // Item persisté : PATCH /items/:id pour refléter le nouveau placement.
            var body = new UpdateItemBody {
                expectedVersion = itemCtx.Version,
                placeId         = toCtx.PlaceId,
                stateData       = toCtx.HasSlotIndex
                    ? new Dictionary<string, object> { { "slotIndex", msg.ToSlotIndex } }
                    : null,
            };
            GameLogger.Network.Info("MoveItem PATCH start uuid={Uuid} version={Version} from={From} to={To} slot={Slot}",
                itemCtx.ItemUuid, itemCtx.Version, msg.FromPlaceId, msg.ToPlaceId, msg.ToSlotIndex);
            UnityWebRequest req = ApiManager.Instance.UpdateItemRequest(itemCtx.ItemUuid, body);
            yield return req.SendWebRequest();
            if (req.responseCode < 200 || req.responseCode >= 300) {
                Debug.LogWarning($"[ServerItemManager] MoveItem PATCH failed code={req.responseCode} body={req.downloadHandler?.text}");
                conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = $"Échec persistance ({req.responseCode})" });
                PushSnapshotsFor(conn, fromCtx, toCtx);
                yield break;
            }
            GameLogger.Network.Info("MoveItem PATCH ok uuid={Uuid} body={Body}", itemCtx.ItemUuid, req.downloadHandler?.text);
            try {
                var item = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text);
                if (item != null) newVersion = item.version;
            } catch { /* tolerate */ }
        } else {
            GameLogger.Network.Info("MoveItem skip PATCH (éphémère) entity={EntityId} from={From} to={To}",
                msg.EntityId, msg.FromPlaceId, msg.ToPlaceId);
        }

        // Apply : tear-down origine → install destination. Les contextes encapsulent
        // les détails (broadcast S2C_*, runtime room/hand state, session entries).
        fromCtx.TearDown(msg.EntityId);
        toCtx.Install(msg.EntityId, itemCtx, msg.ToSlotIndex, newVersion);

        conn.Send(new S2C_MoveItemResult { Success = true, EntityId = msg.EntityId });
        PushSnapshotsFor(conn, fromCtx, toCtx);
    }

    // ── Swap items (atomic two-PATCH) ────────────────────────────────────────
    // Pour hand↔container et container↔container (même placeId).
    // Le hand↔hand passe par C2S_RequestSwapHands (chemin existant).

    public void HandleSwapItems(NetworkConnectionToClient conn, C2S_SwapItems msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        var player = conn.identity.GetComponent<Sim.PlayerController>();
        string charId = player?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityIdA, ErrorMessage = "Sans caractère" });
            return;
        }

        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        var ctxA = ResolvePlace(conn, netId, charId, msg.PlaceIdA, roomId);
        var ctxB = ResolvePlace(conn, netId, charId, msg.PlaceIdB, roomId);
        if (ctxA == null || ctxB == null) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityIdA, ErrorMessage = "Place inconnue (swap)" });
            return;
        }

        // Le hand↔hand est intercepté côté client (PlayerHands.Swap → C2S_RequestSwapHands)
        // et n'arrive pas ici. On tolère néanmoins un éventuel reroute.

        if (!ctxA.TryResolveItem(msg.EntityIdA, msg.SlotIndexA, out var itemA, out var err)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityIdA, ErrorMessage = err });
            PushSnapshotsFor(conn, ctxA, ctxB);
            return;
        }
        if (!ctxB.TryResolveItem(msg.EntityIdB, msg.SlotIndexB, out var itemB, out err)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityIdB, ErrorMessage = err });
            PushSnapshotsFor(conn, ctxA, ctxB);
            return;
        }

        // A va vers ctxB/slotB, B va vers ctxA/slotA. On valide type + bornes des deux
        // destinations. Pas de check d'occupancy : les deux items vont être torn-down
        // avant install, donc les slots sont libres au moment du install.
        if (!ctxB.ValidateAsTarget(msg.SlotIndexB, itemA.ConfigId, out err)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityIdA, ErrorMessage = err });
            PushSnapshotsFor(conn, ctxA, ctxB);
            return;
        }
        if (!ctxA.ValidateAsTarget(msg.SlotIndexA, itemB.ConfigId, out err)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityIdB, ErrorMessage = err });
            PushSnapshotsFor(conn, ctxA, ctxB);
            return;
        }

        // Item éphémère (no DB row → no UUID) qui irait vers autre chose qu'une main :
        // refus en amont. Sinon SwapItemsCoroutine tente une PATCH avec un UUID null
        // qui foire avec une 4xx et le client voit un toast "Échec persistance A"
        // au lieu du vrai motif.
        static bool IsHand(PlaceKind k) => k == PlaceKind.HandLeft || k == PlaceKind.HandRight;
        bool aEphemeralBadTarget = string.IsNullOrEmpty(itemA.ItemUuid) && !IsHand(ctxB.Kind);
        bool bEphemeralBadTarget = string.IsNullOrEmpty(itemB.ItemUuid) && !IsHand(ctxA.Kind);
        if (aEphemeralBadTarget || bEphemeralBadTarget) {
            int rejectedEntity = aEphemeralBadTarget ? msg.EntityIdA : msg.EntityIdB;
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = rejectedEntity,
                ErrorMessage = InventoryToasts.NotStorable });
            PushSnapshotsFor(conn, ctxA, ctxB);
            return;
        }

        ApiManager.Instance?.StartCoroutine(SwapItemsCoroutine(conn, msg, ctxA, itemA, ctxB, itemB));
    }

    private IEnumerator SwapItemsCoroutine(NetworkConnectionToClient conn, C2S_SwapItems msg,
        IPlaceContext ctxA, ItemCtx itemA, IPlaceContext ctxB, ItemCtx itemB)
    {
        // PATCH A → destination de B (placeB / slotB)
        var bodyA = new UpdateItemBody {
            expectedVersion = itemA.Version,
            placeId         = ctxB.PlaceId,
            stateData       = ctxB.HasSlotIndex
                ? new Dictionary<string, object> { { "slotIndex", msg.SlotIndexB } }
                : null,
        };
        UnityWebRequest reqA = ApiManager.Instance.UpdateItemRequest(itemA.ItemUuid, bodyA);
        yield return reqA.SendWebRequest();
        if (reqA.responseCode < 200 || reqA.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] SwapItems PATCH A failed code={reqA.responseCode} body={reqA.downloadHandler?.text}");
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityIdA, ErrorMessage = $"Échec persistance A ({reqA.responseCode})" });
            PushSnapshotsFor(conn, ctxA, ctxB);
            yield break;
        }
        int newVersionA = itemA.Version + 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(reqA.downloadHandler.text); if (it != null) newVersionA = it.version; } catch { }

        // PATCH B → destination de A (placeA / slotA)
        var bodyB = new UpdateItemBody {
            expectedVersion = itemB.Version,
            placeId         = ctxA.PlaceId,
            stateData       = ctxA.HasSlotIndex
                ? new Dictionary<string, object> { { "slotIndex", msg.SlotIndexA } }
                : null,
        };
        UnityWebRequest reqB = ApiManager.Instance.UpdateItemRequest(itemB.ItemUuid, bodyB);
        yield return reqB.SendWebRequest();
        if (reqB.responseCode < 200 || reqB.responseCode >= 300) {
            // Rollback de A : remet itemA à sa place d'origine. Si ce rollback échoue
            // aussi, l'état DB diverge ; le snapshot client le détectera au prochain
            // open du conteneur via re-fetch /places/:id/state.
            Debug.LogWarning($"[ServerItemManager] SwapItems PATCH B failed code={reqB.responseCode} body={reqB.downloadHandler?.text} — rollback A");
            var rollback = new UpdateItemBody {
                expectedVersion = newVersionA,
                placeId         = ctxA.PlaceId,
                stateData       = ctxA.HasSlotIndex
                    ? new Dictionary<string, object> { { "slotIndex", msg.SlotIndexA } }
                    : null,
            };
            UnityWebRequest reqRb = ApiManager.Instance.UpdateItemRequest(itemA.ItemUuid, rollback);
            yield return reqRb.SendWebRequest();
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityIdB, ErrorMessage = $"Échec persistance B ({reqB.responseCode})" });
            PushSnapshotsFor(conn, ctxA, ctxB);
            yield break;
        }
        int newVersionB = itemB.Version + 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(reqB.downloadHandler.text); if (it != null) newVersionB = it.version; } catch { }

        // Tear-down d'abord les DEUX origines, puis install aux destinations swap.
        // Cet ordre garantit que les slots cibles sont libres au moment du install.
        ctxA.TearDown(msg.EntityIdA);
        ctxB.TearDown(msg.EntityIdB);
        ctxB.Install(msg.EntityIdA, itemA, msg.SlotIndexB, newVersionA);
        ctxA.Install(msg.EntityIdB, itemB, msg.SlotIndexA, newVersionB);

        conn.Send(new S2C_MoveItemResult { Success = true, EntityId = msg.EntityIdA });
        PushSnapshotsFor(conn, ctxA, ctxB);

        GameLogger.Network.Info("SwapItems ok netId={NetId} A={A}({KA}/{SA}) ↔ B={B}({KB}/{SB})",
            conn.identity.netId, msg.EntityIdA, ctxA.Kind, msg.SlotIndexA, msg.EntityIdB, ctxB.Kind, msg.SlotIndexB);
    }

    private static void SendContainerSnapshot(NetworkConnectionToClient conn, ContainerSession session)
    {
        var items = new List<S2C_ContainerItem>(session.ItemsByEntityId.Count);
        foreach (var it in session.ItemsByEntityId.Values) {
            items.Add(new S2C_ContainerItem {
                EntityId = it.EntityId, ConfigId = it.ConfigId, SlotIndex = it.SlotIndex,
            });
        }
        conn.Send(new S2C_ContainerOpened {
            PropId        = session.PropId,
            PlaceId       = session.PlaceId,
            SlotCount     = session.Config.SlotCount,
            AcceptedTypes = session.Config.AcceptedTypes.Select(t => (byte)t).ToArray(),
            Items         = items.ToArray(),
        });
    }

    // ── Pocket sessions ─────────────────────────────────────────────────────

    /// <summary>
    /// À appeler après <see cref="PlayerInventory.EnsurePlaces"/> côté serveur :
    /// lit la place poche en DB, alloue des entityId éphémères pour ses items,
    /// envoie le snapshot S2C_PocketSync au client. Idempotent — appel répété
    /// recharge l'état.
    /// </summary>
    public IEnumerator EnsurePocketSession(NetworkConnectionToClient conn) {
        if (conn?.identity == null) yield break;
        uint netId = conn.identity.netId;
        var inv = conn.identity.GetComponent<PlayerInventory>();
        if (inv == null || string.IsNullOrEmpty(inv.PocketPlaceId)) {
            Debug.LogWarning("[ServerItemManager] EnsurePocketSession skipped — PlayerInventory ou PocketPlaceId manquant");
            yield break;
        }

        // Si une session précédente existe (reconnect), libère ses bridges éphémères
        // avant de re-allouer — sinon entityId zombies.
        CleanupPocketSession(netId);

        UnityWebRequest req = ApiManager.Instance.GetPlaceStateRequest(inv.PocketPlaceId);
        yield return req.SendWebRequest();
        if (req.responseCode != 200) {
            Debug.LogWarning($"[ServerItemManager] EnsurePocketSession GET failed code={req.responseCode}");
            yield break;
        }

        Debug.Log($"[ServerItemManager] PocketState raw response (place={inv.PocketPlaceId}): {req.downloadHandler.text}");

        PlaceStateJson state = null;
        try { state = JsonConvert.DeserializeObject<PlaceStateJson>(req.downloadHandler.text); }
        catch (System.Exception e) { Debug.LogWarning($"[ServerItemManager] PocketState parse: {e.Message}"); yield break; }

        var session = new PocketSession { PlaceId = inv.PocketPlaceId };
        int itemsFromDb = state?.items?.Length ?? 0;
        if (state?.items != null) {
            foreach (var it in state.items) {
                int slotIndex = ReadSlotIndex(it.stateData);
                int entityId  = _nextEntityId++;
                session.ItemsByEntityId[entityId] = new PocketSessionItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex,
                    ItemUuid = it.Id, Version = it.version,
                };
                if (!session.SlotToEntityId.ContainsKey(slotIndex))
                    session.SlotToEntityId[slotIndex] = entityId;
                _bridges[entityId] = new ItemDbBridge {
                    Uuid = it.Id, Version = it.version, PlaceId = inv.PocketPlaceId,
                };
                Debug.Log($"[ServerItemManager] Pocket item loaded id={it.Id} cfg={it.configId} slot={slotIndex} entityId={entityId}");
            }
        }
        _pocketByPlayer[netId] = session;

        SendPocketSnapshot(conn, session);
        Debug.Log($"[ServerItemManager] PocketSessionOpened netId={netId} place={inv.PocketPlaceId} itemsFromDb={itemsFromDb} sessionItems={session.ItemsByEntityId.Count}");
    }

    private static void SendPocketSnapshot(NetworkConnectionToClient conn, PocketSession session) {
        Debug.Log($"[ServerItemManager] SendPocketSnapshot place={session.PlaceId} sessionItems={session.ItemsByEntityId.Count} conn={conn?.connectionId}");
        var items = new List<S2C_PocketItem>(session.ItemsByEntityId.Count);
        foreach (var it in session.ItemsByEntityId.Values) {
            items.Add(new S2C_PocketItem {
                EntityId = it.EntityId, ConfigId = it.ConfigId, SlotIndex = it.SlotIndex,
            });
        }
        conn.Send(new S2C_PocketSync {
            PlaceId   = session.PlaceId,
            SlotCount = PocketCapacity,
            Items     = items.ToArray(),
        });
    }

    private void CleanupPocketSession(uint netId) {
        if (!_pocketByPlayer.TryGetValue(netId, out var session)) return;
        _pocketByPlayer.Remove(netId);
        foreach (var it in session.ItemsByEntityId.Values) _bridges.Remove(it.EntityId);
        GameLogger.Network.Debug("PocketSessionClosed netId={NetId} place={Place}", netId, session.PlaceId);
    }

    // ── Strategy pattern : IPlaceContext + concrete contexts ─────────────────
    // Chaque PlaceKind expose les opérations move/swap derrière une interface
    // commune. HandleMoveItem / HandleSwapItems sont alors place-agnostiques :
    // ils orchestrent PATCH + TearDown + Install + snapshots via l'interface.
    //
    // Pour ajouter un nouveau type de place (backpack, item-as-container) :
    //   1. Créer une classe concrète qui implémente IPlaceContext.
    //   2. Ajouter UNE ligne dans ResolvePlace pour la factory.
    //   3. Si elle a sa propre session ; ajouter sa publication dans PushSnapshotsFor.
    // → Aucun touch à HandleMoveItem / HandleSwapItems.

    /// <summary>
    /// Données minimales pour identifier un item (uuid DB + version OCC + ItemConfig.ID).
    /// Résolu par <see cref="IPlaceContext.TryResolveItem"/>, consommé par les coroutines PATCH.
    /// </summary>
    private struct ItemCtx {
        public int    ConfigId;
        public string ItemUuid;
        public int    Version;
    }

    private interface IPlaceContext {
        PlaceKind Kind          { get; }
        string    PlaceId       { get; }   // UUID backend (résolu, prêt à passer dans UpdateItemBody.placeId)
        bool      HasSlotIndex  { get; }   // true = stateData.slotIndex est requis dans le PATCH

        bool TryResolveItem(int entityId, int declaredSlot, out ItemCtx ctx, out string err);
        bool ValidateAsTarget(int slotIndex, int configId, out string err);
        bool IsSlotAvailableFor(int slotIndex, int entityId);
        void TearDown(int entityId);
        void Install(int entityId, ItemCtx ctx, int slotIndex, int newVersion);
    }

    private sealed class HandPlaceContext : IPlaceContext {
        private readonly ServerItemManager _mgr;
        private readonly NetworkConnectionToClient _conn;
        private readonly uint _netId;
        private readonly HandType _hand;
        private readonly string _roomId;
        private readonly string _backendPlaceId;

        public HandPlaceContext(ServerItemManager mgr, NetworkConnectionToClient conn, uint netId,
            HandType hand, string roomId, string backendPlaceId)
        {
            _mgr = mgr; _conn = conn; _netId = netId; _hand = hand;
            _roomId = roomId; _backendPlaceId = backendPlaceId;
        }

        public PlaceKind Kind         => _hand == HandType.Left ? PlaceKind.HandLeft : PlaceKind.HandRight;
        public string    PlaceId      => _backendPlaceId;
        public bool      HasSlotIndex => false;

        public bool TryResolveItem(int entityId, int declaredSlot, out ItemCtx ctx, out string err) {
            ctx = default; err = null;
            if (!_mgr._playerHands.TryGetValue(_netId, out var hands) || hands.GetEntityId(_hand) != entityId) {
                err = "Item pas dans cette main"; return false;
            }
            if (!_mgr.TryGetEntity(_roomId, entityId, out var entity)) {
                err = "Entity introuvable"; return false;
            }
            // Bridge optionnel : un item éphémère (WASTE collecté, sac poubelle, colis
            // mission) n'a PAS de ligne DB → bridge null. C'est valide pour un move
            // hand↔hand, le coroutine de move skip simplement la PATCH quand l'UUID est
            // vide. Pour des destinations persistantes (pocket, container), la conversion
            // ephemeral→persistent demanderait un POST /items qu'on ne gère pas encore →
            // ces moves seront filtrés en amont (ValidateAsTarget côté destination).
            var bridge = _mgr.GetBridge(entityId);
            string uuid = bridge?.Uuid;
            int version = bridge?.Version ?? 0;
            ctx = new ItemCtx { ConfigId = entity.ItemConfigId, ItemUuid = uuid, Version = version };
            return true;
        }

        public bool ValidateAsTarget(int slotIndex, int configId, out string err) {
            err = null;
            // Garde TWO_HAND : un item à deux mains nécessite que l'AUTRE main soit
            // libre ; et inversement, si l'autre main porte un TWO_HAND, cette main
            // ne peut accueillir aucun item (cohérence d'état serveur, sinon le
            // client peut casser le système en équipant un second item).
            var cfg = DatabaseManager.GetItemConfigById(configId);
            bool incomingTwoHand = cfg != null && cfg.HandleType == ItemHandleType.TWO_HAND;
            if (!_mgr._playerHands.TryGetValue(_netId, out var hands)) return true;
            HandType otherHand = _hand == HandType.Left ? HandType.Right : HandType.Left;
            int otherHandId = hands.GetEntityId(otherHand);
            if (incomingTwoHand) {
                if (otherHandId != -1) {
                    err = InventoryToasts.OtherHandBusy;
                    return false;
                }
            } else if (otherHandId != -1) {
                if (_mgr._rooms.TryGetValue(_roomId, out var roomItems)
                    && roomItems.TryGetValue(otherHandId, out var otherEntity)) {
                    var otherCfg = DatabaseManager.GetItemConfigById(otherEntity.ItemConfigId);
                    if (otherCfg != null && otherCfg.HandleType == ItemHandleType.TWO_HAND) {
                        err = InventoryToasts.AlreadyHoldingTwoHand;
                        return false;
                    }
                }
            }
            return true;
        }

        public bool IsSlotAvailableFor(int slotIndex, int entityId) {
            if (!_mgr._playerHands.TryGetValue(_netId, out var hs)) return true;
            int current = hs.GetEntityId(_hand);
            return current == -1 || current == entityId;
        }

        public void TearDown(int entityId) {
            if (_mgr._playerHands.TryGetValue(_netId, out var hs)) {
                hs.Clear(_hand); _mgr._playerHands[_netId] = hs;
            }
            if (_mgr._rooms.TryGetValue(_roomId, out var roomItems) && roomItems.ContainsKey(entityId)) {
                roomItems.Remove(entityId);
                BroadcastToRoom(_roomId, new S2C_DestroyItem { EntityId = entityId, RoomId = _roomId });
            }
        }

        public void Install(int entityId, ItemCtx ctx, int slotIndex, int newVersion) {
            var entity = new ItemEntity {
                EntityId = entityId, RoomId = _roomId, ItemConfigId = ctx.ConfigId,
                Position = _conn.identity.transform.position, Rotation = Quaternion.identity,
                HolderNetId = _netId, HolderHand = _hand,
                LocalPosition = Vector3.zero, LocalRotation = Quaternion.identity,
                Persistent = true,
            };
            if (!_mgr._rooms.TryGetValue(_roomId, out var roomItems)) {
                roomItems = new Dictionary<int, ItemEntity>(); _mgr._rooms[_roomId] = roomItems;
            }
            roomItems[entityId] = entity;
            var hs = _mgr.GetOrCreateHandState(_netId);
            hs.Set(_hand, entityId);
            _mgr._playerHands[_netId] = hs;
            _mgr._bridges[entityId] = new ItemDbBridge { Uuid = ctx.ItemUuid, Version = newVersion, PlaceId = _backendPlaceId };
            BroadcastToRoom(_roomId, new S2C_SpawnItem {
                EntityId = entityId, RoomId = _roomId, ItemConfigId = ctx.ConfigId,
                Position = entity.Position, Rotation = entity.Rotation,
                IsHeld = true, HolderNetId = _netId, HolderHand = _hand,
                LocalPosition = Vector3.zero, LocalRotation = Quaternion.identity,
            });
        }
    }

    private sealed class ContainerPlaceContext : IPlaceContext {
        private readonly ServerItemManager _mgr;
        private readonly ContainerSession  _session;

        public ContainerPlaceContext(ServerItemManager mgr, ContainerSession session) { _mgr = mgr; _session = session; }

        public PlaceKind Kind         => PlaceKind.Container;
        public string    PlaceId      => _session.PlaceId;
        public bool      HasSlotIndex => true;

        public bool TryResolveItem(int entityId, int declaredSlot, out ItemCtx ctx, out string err) {
            ctx = default; err = null;
            if (!_session.ItemsByEntityId.TryGetValue(entityId, out var si)) {
                err = "Item conteneur introuvable"; return false;
            }
            if (declaredSlot >= 0 && si.SlotIndex != declaredSlot) {
                err = "Slot conteneur incohérent"; return false;
            }
            ctx = new ItemCtx { ConfigId = si.ConfigId, ItemUuid = si.ItemUuid, Version = si.Version };
            return true;
        }

        public bool ValidateAsTarget(int slotIndex, int configId, out string err) {
            err = null;
            var itemConfig = DatabaseManager.ItemConfigs.Find(x => x.ID == configId);
            if (itemConfig != null && !_session.Config.Accepts(itemConfig.Type)) {
                err = "Type refusé par ce conteneur"; return false;
            }
            if (slotIndex < 0 || slotIndex >= _session.Config.SlotCount) {
                err = "Slot hors limites"; return false;
            }
            return true;
        }

        public bool IsSlotAvailableFor(int slotIndex, int entityId) {
            if (!_session.SlotToEntityId.TryGetValue(slotIndex, out var occ)) return true;
            return occ == entityId;
        }

        public void TearDown(int entityId) {
            if (!_session.ItemsByEntityId.TryGetValue(entityId, out var old)) return;
            _session.ItemsByEntityId.Remove(entityId);
            if (_session.SlotToEntityId.TryGetValue(old.SlotIndex, out var occ) && occ == entityId)
                _session.SlotToEntityId.Remove(old.SlotIndex);
        }

        public void Install(int entityId, ItemCtx ctx, int slotIndex, int newVersion) {
            _session.ItemsByEntityId[entityId] = new ContainerSessionItem {
                EntityId = entityId, ConfigId = ctx.ConfigId, SlotIndex = slotIndex,
                ItemUuid = ctx.ItemUuid, Version = newVersion,
            };
            _session.SlotToEntityId[slotIndex] = entityId;
            _mgr._bridges[entityId] = new ItemDbBridge { Uuid = ctx.ItemUuid, Version = newVersion, PlaceId = _session.PlaceId };
        }
    }

    private sealed class PocketPlaceContext : IPlaceContext {
        private readonly ServerItemManager _mgr;
        private readonly uint   _netId;
        private readonly string _backendPlaceId;

        public PocketPlaceContext(ServerItemManager mgr, uint netId, string backendPlaceId) {
            _mgr = mgr; _netId = netId; _backendPlaceId = backendPlaceId;
        }

        public PlaceKind Kind         => PlaceKind.Pocket;
        public string    PlaceId      => _backendPlaceId;
        public bool      HasSlotIndex => true;

        public bool TryResolveItem(int entityId, int declaredSlot, out ItemCtx ctx, out string err) {
            ctx = default; err = null;
            if (!_mgr._pocketByPlayer.TryGetValue(_netId, out var s) || !s.ItemsByEntityId.TryGetValue(entityId, out var pi)) {
                err = "Item poche introuvable"; return false;
            }
            if (declaredSlot >= 0 && pi.SlotIndex != declaredSlot) {
                err = "Slot poche incohérent"; return false;
            }
            ctx = new ItemCtx { ConfigId = pi.ConfigId, ItemUuid = pi.ItemUuid, Version = pi.Version };
            return true;
        }

        public bool ValidateAsTarget(int slotIndex, int configId, out string err) {
            err = null;
            if (slotIndex < 0 || slotIndex >= PocketCapacity) {
                err = "Slot poche hors limites"; return false;
            }
            var itemConfig = DatabaseManager.ItemConfigs.Find(x => x.ID == configId);
            if (itemConfig != null && !itemConfig.AllowedInPocket) {
                err = InventoryToasts.NotPocketable; return false;
            }
            return true;
        }

        public bool IsSlotAvailableFor(int slotIndex, int entityId) {
            if (!_mgr._pocketByPlayer.TryGetValue(_netId, out var s)) return true;
            if (!s.SlotToEntityId.TryGetValue(slotIndex, out var occ)) return true;
            return occ == entityId;
        }

        public void TearDown(int entityId) {
            if (!_mgr._pocketByPlayer.TryGetValue(_netId, out var s)) return;
            if (!s.ItemsByEntityId.TryGetValue(entityId, out var old)) return;
            s.ItemsByEntityId.Remove(entityId);
            if (s.SlotToEntityId.TryGetValue(old.SlotIndex, out var occ) && occ == entityId)
                s.SlotToEntityId.Remove(old.SlotIndex);
        }

        public void Install(int entityId, ItemCtx ctx, int slotIndex, int newVersion) {
            if (!_mgr._pocketByPlayer.TryGetValue(_netId, out var s)) {
                // Cas pathologique : move avant EnsurePocketSession. On crée à la volée.
                s = new PocketSession { PlaceId = _backendPlaceId };
                _mgr._pocketByPlayer[_netId] = s;
            }
            s.ItemsByEntityId[entityId] = new PocketSessionItem {
                EntityId = entityId, ConfigId = ctx.ConfigId, SlotIndex = slotIndex,
                ItemUuid = ctx.ItemUuid, Version = newVersion,
            };
            s.SlotToEntityId[slotIndex] = entityId;
            _mgr._bridges[entityId] = new ItemDbBridge { Uuid = ctx.ItemUuid, Version = newVersion, PlaceId = _backendPlaceId };
        }
    }

    /// <summary>
    /// Factory : convertit un placeId wire (placeKey "hand_left:…/pocket:…" ou UUID conteneur)
    /// en l'IPlaceContext approprié, en résolvant le backend UUID via PlayerInventory.
    /// Retourne null si le placeId n'est reconnu pour ce joueur (Unknown place).
    /// </summary>
    private IPlaceContext ResolvePlace(NetworkConnectionToClient conn, uint netId, string charId,
        string placeId, string roomId)
    {
        if (string.IsNullOrEmpty(placeId) || string.IsNullOrEmpty(charId)) return null;
        var inv = conn?.identity?.GetComponent<PlayerInventory>();

        if (placeId == $"hand_left:{charId}") {
            string backend = inv?.HandLeftPlaceId ?? placeId;
            return new HandPlaceContext(this, conn, netId, HandType.Left, roomId, backend);
        }
        if (placeId == $"hand_right:{charId}") {
            string backend = inv?.HandRightPlaceId ?? placeId;
            return new HandPlaceContext(this, conn, netId, HandType.Right, roomId, backend);
        }
        if (placeId == $"pocket:{charId}") {
            string backend = inv?.PocketPlaceId ?? placeId;
            return new PocketPlaceContext(this, netId, backend);
        }
        if (_openContainerByPlayer.TryGetValue(netId, out var openContainer) && placeId == openContainer.PlaceId) {
            return new ContainerPlaceContext(this, openContainer);
        }
        return null;
    }

    /// <summary>
    /// Re-pousse les snapshots client après une opération de move/swap, pour les
    /// sessions concernées (container, pocket). À étendre quand on ajoute backpack
    /// / item-as-container — une ligne par session-kind.
    /// </summary>
    private void PushSnapshotsFor(NetworkConnectionToClient conn, IPlaceContext from, IPlaceContext to) {
        uint netId = conn.identity.netId;
        if ((from.Kind == PlaceKind.Container || to.Kind == PlaceKind.Container)
            && _openContainerByPlayer.TryGetValue(netId, out var container)) {
            SendContainerSnapshot(conn, container);
        }
        if ((from.Kind == PlaceKind.Pocket || to.Kind == PlaceKind.Pocket)
            && _pocketByPlayer.TryGetValue(netId, out var pocket)) {
            SendPocketSnapshot(conn, pocket);
        }
    }
}
