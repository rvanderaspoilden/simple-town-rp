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
        public string ItemUuid;       // pour un prop emballé (IsProp) : l'UUID du PROP (table props)
        public int    Version;
        public bool   IsProp;         // true = meuble emballé (ligne props déplacée), pas un item
        public int    PropConfigId;   // PropsConfig.id quand IsProp (icône + déballage)
        public int    PropPresetId;   // variant du meuble quand IsProp (preview au déballage)
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
        public bool   IsVehicleTrunk; // true = coffre de véhicule (PropUuid = uuid véhicule, pas de prop)
        public Dictionary<int, ContainerSessionItem> ItemsByEntityId = new Dictionary<int, ContainerSessionItem>();
        public Dictionary<int, int> SlotToEntityId = new Dictionary<int, int>();
    }

    /// <summary>Émis (serveur) quand le coffre d'un véhicule s'ouvre/se ferme. Argument : uuid du
    /// véhicule (= clé "container:{uuid}") + état ouvert. <see cref="VehicleController"/> s'y abonne
    /// pour jouer le son de coffre. Pas de prop à animer → pas de S2C_ContainerVisualState.</summary>
    public static event System.Action<string, bool> OnVehicleTrunkStateChanged;

    private readonly Dictionary<uint, ContainerSession> _openContainerByPlayer
        = new Dictionary<uint, ContainerSession>();

    // ── Item-as-container sessions (« package ») ──────────────────────────────
    // Un item tenu en main peut être lui-même un conteneur. Place backend
    // "item_container:{itemUuid}" (owner_id = uuid de l'item). Même machinerie que
    // ContainerSession ; on réutilise ContainerSessionItem pour les entrées. RoomId =
    // room de l'opener (opener == holder : on ouvre son propre item). Un seul conteneur
    // (prop OU item) ouvert à la fois par joueur → partagé via CloseSession.

    private sealed class ItemContainerSession {
        public int    PackageEntityId;
        public string PackageItemUuid;
        public string PlaceId;
        public string RoomId;
        public uint   OpenedBy;
        public ContainerConfig Config;
        public Dictionary<int, ContainerSessionItem> ItemsByEntityId = new Dictionary<int, ContainerSessionItem>();
        public Dictionary<int, int> SlotToEntityId = new Dictionary<int, int>();
    }

    private readonly Dictionary<uint, ItemContainerSession> _openItemContainerByPlayer
        = new Dictionary<uint, ItemContainerSession>();

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

        // Un item adossé à la DB est PAR DÉFINITION persistant : aux transitions de room
        // (et au reconnect) il doit être détruit-en-gardant-la-DB puis restauré depuis la
        // DB — surtout PAS traité comme un éphémère, ce qui le re-spawnerait sans bridge
        // et romprait le lien "item_container:{uuid}" d'un colis (open/pickup échouent).
        // Point unique : toute création de bridge (restore main, item-monde DB, upsert
        // pickup) passe ici, donc le flag est garanti cohérent.
        foreach (var room in _rooms.Values)
            if (room.TryGetValue(entityId, out var e)) { e.Persistent = true; break; }
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
        _openItemContainerByPlayer.Clear();
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
        HandType? hand  = ResolveHand(resolvedCfg, playerNetId);

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

        // Besoin spécifique (bidon d'essence) → sous-classe dédiée, sans polluer ItemEntity.
        // Le bidon spawn PLEIN à la capacité définie dans sa config (FuelCanisterConfig).
        ItemEntity entity;
        if (itemConfigId == FuelCanister.ConfigId) {
            float capacity = (DatabaseManager.GetItemConfigById(itemConfigId) as FuelCanisterConfig)?.fuelCapacity ?? 20f;
            entity = new FuelCanisterEntity {
                EntityId = entityId, RoomId = roomId, ItemConfigId = itemConfigId,
                Position = position, Rotation = rotation, Fuel = capacity };
        } else {
            entity = new ItemEntity {
                EntityId = entityId, RoomId = roomId, ItemConfigId = itemConfigId,
                Position = position, Rotation = rotation };
        }

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

        // Bidon : diffuse son niveau de carburant aux clients présents (sinon ils affichent 0).
        if (entity is FuelCanisterEntity fc)
            BroadcastToRoom(roomId, new S2C_ItemFuel { EntityId = entityId, Fuel = fc.Fuel });

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
    /// Server boot: load every position-bearing world item stored under a place
    /// into the given runtime room, re-bridged so a later pickup moves/deletes the
    /// right DB row. Mirrors ApartmentController's ground-item restore for the City,
    /// which has no ApartmentController of its own.
    /// </summary>
    public IEnumerator LoadWorldItemsForPlace(string roomId, string placeId)
    {
        if (ApiManager.Instance == null || string.IsNullOrEmpty(placeId)) yield break;

        UnityWebRequest req = ApiManager.Instance.GetPlaceStateRequest(placeId);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] LoadWorldItemsForPlace GET state failed code={req.responseCode} place={placeId}");
            yield break;
        }

        PlaceStateJson state;
        try { state = JsonConvert.DeserializeObject<PlaceStateJson>(req.downloadHandler.text); }
        catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] LoadWorldItemsForPlace parse error: {e.Message}");
            yield break;
        }
        if (state?.items == null) yield break;

        int count = 0;
        foreach (ItemJson it in state.items) {
            if (it.position == null) continue;        // inventory / stored items have no world position
            if (it.placeId != placeId) continue;

            Vector3    wpos = it.position.ToVector3();
            Quaternion wrot = Quaternion.Euler(it.rotation != null ? it.rotation.ToVector3() : Vector3.zero);
            SpawnPersistentWorldFromDb(roomId, it.configId, wpos, wrot, it.Id, it.version, placeId);
            count++;
        }
        GameLogger.Network.Info("World items loaded count={Count} place={Place} room={Room}", count, placeId, roomId);
    }

    /// <summary>
    /// Removes a world item from the room and tells all room clients to destroy it.
    /// Also drops the item from its holder's hand if currently held.
    /// </summary>
    public void DespawnItem(string roomId, int entityId)
    {
        if (!TryGetEntity(roomId, entityId, out var entity)) return;

        // Supprime la ligne DB si elle existe (no-op pour les items éphémères).
        // Sans ça, un item consommé/admin-destroyed survivait au reconnect. Si l'item est un
        // conteneur (colis), le backend cascade la suppression de son contenu (items + meubles
        // emballés) et de sa place — DELETE /items/:id (ItemService.delete → deleteOwnedContainer).
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
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Pas dans une pièce" });
            return;
        }

        if (!TryGetEntity(roomId, msg.EntityId, out var entity))
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Objet introuvable" });
            return;
        }

        if (entity.IsHeld)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Objet déjà tenu" });
            return;
        }

        if (entity.AuthorizedNetId != 0 && entity.AuthorizedNetId != playerNetId)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Cet objet ne vous appartient pas" });
            return;
        }

        // Distance check
        Vector3 playerPos = conn.identity.transform.position;
        if (Vector3.Distance(playerPos, entity.Position) > 3f)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Trop loin" });
            return;
        }

        // Find a free hand
        var handState = GetOrCreateHandState(playerNetId);
        ItemConfig config = DatabaseManager.ItemConfigs.Find(x => x.ID == entity.ItemConfigId);

        HandType? assignedHand = ResolveHand(config, playerNetId);
        if (!assignedHand.HasValue)
        {
            // Mains pleines : si le joueur tient un colis (item-conteneur), l'item ramassé
            // va dans un slot libre du colis (même fermé). Plein → toast + pas de ramassage.
            if (TryGetHeldPackage(playerNetId, roomId, out _, out string pkgUuid, out ContainerConfig pkgCfg)) {
                int worldEntityId = msg.EntityId;
                int worldConfigId = entity.ItemConfigId;
                string charIdPk = conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id;
                ApiManager.Instance?.StartCoroutine(CreateItemInPackageCoroutine(conn, playerNetId, worldConfigId, charIdPk, pkgUuid, pkgCfg,
                    onSuccess: () => {
                        DespawnItem(roomId, worldEntityId);
                        conn.Send(new S2C_PickupResult { Success = true, EntityId = worldEntityId });
                    },
                    onFail: err => conn.Send(new S2C_PickupResult { Success = false, EntityId = worldEntityId, ErrorMessage = err })));
                return;
            }
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Mains pleines" });
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
        if (GetBridge(msg.EntityId) != null) {
            ItemConfig pickCfg = DatabaseManager.GetItemConfigById(entity.ItemConfigId);
            if (pickCfg != null && pickCfg.ToPersist) {
                // Item-monde persistant ramassé (UNIFORME) : on PATCH sa place vers la main,
                // UUID conservé (jamais de delete) → la place "item_container:{uuid}" d'un
                // colis et son contenu survivent au pickup.
                var invC = conn.identity.GetComponent<PlayerInventory>();
                string handPlaceId = invC?.HandPlaceFor(assignedHand.Value);
                if (!string.IsNullOrEmpty(handPlaceId)) PersistMoveToHandAsync(msg.EntityId, handPlaceId);
            } else {
                PersistDropAsync(msg.EntityId); // bridgé non-persistant → delete (historique)
            }
        }
        else if (entity.Persistent)
            PersistPickupAsync(conn, msg.EntityId, entity.ItemConfigId, assignedHand.Value);
    }

    // ── Carburant générique d'item (bidon d'essence) ────────────────────────────

    /// <summary>
    /// Transfère du carburant depuis l'item de config <paramref name="configId"/> tenu par le
    /// joueur. <paramref name="hasItem"/> = le joueur tient bien cet item ; <paramref name="transferred"/>
    /// = min(réserve du bidon, <paramref name="requested"/>), retiré de sa réserve. Renvoie true si
    /// un transfert effectif (>0) a eu lieu.
    /// </summary>
    public bool TryConsumeHeldFuel(NetworkConnectionToClient conn, int configId, float requested,
        out float transferred, out bool hasItem) {
        transferred = 0f; hasItem = false;
        if (conn?.identity == null) return false;
        uint netId = conn.identity.netId;
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null) return false;
        if (!_playerHands.TryGetValue(netId, out var hands)) return false;

        int[] held = { hands.RightEntityId, hands.LeftEntityId };
        Debug.Log($"[Refuel] netId={netId} room={roomId} cfgWanted={configId} R={hands.RightEntityId} L={hands.LeftEntityId}");
        foreach (int eid in held) {
            if (eid == -1) continue;
            if (!TryGetEntity(roomId, eid, out var e)) { Debug.Log($"[Refuel] eid={eid} introuvable dans room"); continue; }
            Debug.Log($"[Refuel] eid={eid} configId={e.ItemConfigId} type={e.GetType().Name}");
            if (e.ItemConfigId != configId || e is not FuelCanisterEntity fce) continue;
            hasItem = true;
            transferred = Mathf.Min(fce.Fuel, Mathf.Max(0f, requested));
            fce.Fuel -= transferred;
            if (transferred > 0f)
                BroadcastToRoom(roomId, new S2C_ItemFuel { EntityId = eid, Fuel = fce.Fuel });
            return transferred > 0f;
        }
        return false;
    }

    // Le joueur tient-il un colis (item-conteneur persisté) ? Renvoie son uuid + config conteneur.
    // Un objet de stockage (conteneur) ne peut être rangé dans un autre conteneur QUE s'il est
    // VIDE (sinon : "videz-le d'abord"). Un item est un objet de stockage quand sa config expose
    // une grille (SlotCount > 0). Règle appliquée à chaque point de placement (déplacement,
    // swap, emballage de meuble).
    private static bool IsStorageItem(ItemConfig cfg)
        => cfg != null && cfg.Container != null && cfg.Container.IsContainer;

    /// <summary>Le conteneur (item ou prop) identifié par <paramref name="uuid"/> est-il VIDE
    /// (sa place ne contient aucun item ni meuble) ? Place inexistante = vide. Async (lecture DB),
    /// résultat via <paramref name="cb"/>.</summary>
    private IEnumerator CheckContainerEmptyByUuid(string uuid, bool isProp, System.Action<bool> cb) {
        if (string.IsNullOrEmpty(uuid)) { cb(true); yield break; }
        string placeKey = (isProp ? "container:" : "item_container:") + uuid;
        var createBody = new CreatePlaceBody { placeKey = placeKey, type = "container", ownerId = uuid };
        UnityWebRequest createReq = ApiManager.Instance.CreatePlaceRequest(createBody);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode < 200 || createReq.responseCode >= 300) { cb(true); yield break; }
        string placeId = null;
        try { placeId = JsonConvert.DeserializeObject<PlaceJson>(createReq.downloadHandler.text)?.Id; } catch { }
        if (string.IsNullOrEmpty(placeId)) { cb(true); yield break; }
        UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
        yield return stateReq.SendWebRequest();
        if (stateReq.responseCode != 200) { cb(true); yield break; }
        bool empty = true;
        try {
            var sd = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text);
            empty = (sd?.items?.Length ?? 0) + (sd?.props?.Length ?? 0) == 0;
        } catch { }
        cb(empty);
    }

    /// <summary>Gate "conteneur imbriqué" pour un déplacement/swap : si l'item déplacé est un
    /// conteneur ET que la cible est un conteneur (prop ou colis), n'autorise que s'il est vide.
    /// <paramref name="cb"/>(true) = autorisé, (false) = refusé (à vider d'abord).</summary>
    private IEnumerator GateNestedContainer(ItemCtx moved, IPlaceContext toCtx, System.Action<bool> cb) {
        bool intoContainer = toCtx.Kind == PlaceKind.Container || toCtx.Kind == PlaceKind.ItemContainer;
        if (!intoContainer || !IsStorageItem(DatabaseManager.GetItemConfigById(moved.ConfigId))) { cb(true); yield break; }
        // Coffre de véhicule : imbrication autorisée (on peut y ranger un colis rempli).
        if (toCtx is ContainerPlaceContext cpc && cpc.AllowsNesting) { cb(true); yield break; }
        yield return CheckContainerEmptyByUuid(moved.ItemUuid, isProp: false, cb);
    }

    private bool TryGetHeldPackage(uint netId, string roomId, out int pkgEntityId, out string pkgUuid, out ContainerConfig pkgCfg) {
        pkgEntityId = -1; pkgUuid = null; pkgCfg = null;
        if (!_playerHands.TryGetValue(netId, out var hands)) return false;
        int[] held = { hands.RightEntityId, hands.LeftEntityId };
        foreach (int eid in held) {
            if (eid == -1) continue;
            if (!TryGetEntity(roomId, eid, out var e)) continue;
            var cfg = DatabaseManager.GetItemConfigById(e.ItemConfigId);
            if (cfg == null || cfg.Container == null || !cfg.Container.IsContainer) continue;
            var br = GetBridge(eid);
            if (br == null || string.IsNullOrEmpty(br.Uuid)) continue;
            pkgEntityId = eid; pkgUuid = br.Uuid; pkgCfg = cfg.Container; return true;
        }
        return false;
    }

    /// <summary>
    /// Acquisition UNIVERSELLE d'un item dans l'inventaire du joueur : main libre en
    /// priorité, sinon colis tenu (avec slot libre), sinon échec. Point unique pour TOUS
    /// les flux qui donnent un item au joueur (CLEAN, achats dispenser/shop, récompenses…)
    /// → plus besoin de re-vérifier les mains action par action. <paramref name="onSuccess"/>
    /// est appelé une fois l'item réellement placé (synchrone pour la main, après l'aller-retour
    /// backend pour le colis) ; <paramref name="onFail"/> avec un message de toast sinon.
    /// </summary>
    public void SpawnItemIntoInventory(NetworkConnectionToClient conn, int configId, ItemConfig config,
        bool persistent, System.Action onSuccess = null, System.Action<string> onFail = null) {
        if (conn?.identity == null) { onFail?.Invoke("Pas de joueur"); return; }
        uint netId = conn.identity.netId;
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null) { onFail?.Invoke("Pas de pièce"); return; }

        if (ResolveHand(config, netId).HasValue) {
            SpawnItemInHand(roomId, configId, conn, config, persistent);
            onSuccess?.Invoke();
            return;
        }
        if (TryGetHeldPackage(netId, roomId, out _, out string pkgUuid, out ContainerConfig pkgCfg)) {
            string charId = conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id;
            ApiManager.Instance?.StartCoroutine(
                CreateItemInPackageCoroutine(conn, netId, configId, charId, pkgUuid, pkgCfg, onSuccess, onFail));
            return;
        }
        onFail?.Invoke("Mains pleines");
    }

    /// <summary>Pratique : true si le joueur peut recevoir un item (main libre OU colis tenu).
    /// La capacité réelle du colis n'est confirmée qu'à l'écriture (async).</summary>
    public bool CanReceiveItem(NetworkConnectionToClient conn, ItemConfig config) {
        if (conn?.identity == null) return false;
        uint netId = conn.identity.netId;
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null) return false;
        return ResolveHand(config, netId).HasValue
            || TryGetHeldPackage(netId, roomId, out _, out _, out _);
    }

    // Cœur partagé : crée un item (config) dans le colis tenu — ensure place + slot libre +
    // POST item + maj session live. Utilisé par le ramassage ET SpawnItemIntoInventory.
    private IEnumerator CreateItemInPackageCoroutine(NetworkConnectionToClient conn, uint netId, int configId,
        string charId, string pkgUuid, ContainerConfig pkgCfg, System.Action onSuccess, System.Action<string> onFail) {
        string placeKey = $"item_container:{pkgUuid}";
        var createBody = new CreatePlaceBody {
            placeKey = placeKey, type = "container", ownerId = pkgUuid,
            properties = new Dictionary<string, object> {
                { "slotCount",     pkgCfg.SlotCount },
                { "acceptedTypes", pkgCfg.AcceptedTypes.Select(t => (int)t).ToArray() },
            },
        };
        UnityWebRequest createReq = ApiManager.Instance.CreatePlaceRequest(createBody);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode < 200 || createReq.responseCode >= 300) { onFail?.Invoke("Erreur colis"); yield break; }
        PlaceJson placeData = null;
        try { placeData = JsonConvert.DeserializeObject<PlaceJson>(createReq.downloadHandler.text); } catch { }
        string placeId = placeData?.Id;
        if (string.IsNullOrEmpty(placeId)) { onFail?.Invoke("Colis sans place"); yield break; }

        UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
        yield return stateReq.SendWebRequest();
        var occupied = new HashSet<int>();
        if (stateReq.responseCode == 200) {
            try {
                var sd = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text);
                if (sd?.items != null) foreach (var it in sd.items) occupied.Add(ReadSlotIndex(it.stateData));
                // Les meubles emballés occupent aussi des slots → ne pas écraser leur place.
                if (sd?.props != null) foreach (var p in sd.props) occupied.Add(ReadSlotIndex(p.stateData));
            } catch { }
        }
        int freeSlot = -1;
        for (int i = 0; i < pkgCfg.SlotCount; i++) if (!occupied.Contains(i)) { freeSlot = i; break; }
        if (freeSlot < 0) { onFail?.Invoke("Colis plein"); yield break; }

        var itemCfg = DatabaseManager.GetItemConfigById(configId);
        if (IsStorageItem(itemCfg)) { onFail?.Invoke(InventoryToasts.NoNestedStorage); yield break; }
        if (itemCfg != null && !pkgCfg.Accepts(itemCfg.Type)) { onFail?.Invoke("Refusé par le colis"); yield break; }

        var body = new CreateItemBody {
            placeId = placeId, configId = configId, quantity = 1, ownedBy = charId,
            stateData = new Dictionary<string, object> { { "slotIndex", freeSlot } },
        };
        UnityWebRequest req = ApiManager.Instance.CreateItemRequest(body);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300) { onFail?.Invoke("Erreur stockage"); yield break; }
        string itemUuid = null; int version = 1;
        try { ItemJson item = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text); if (item != null) { itemUuid = item.Id; version = item.version; } }
        catch { }

        // Colis ouvert → maj session + snapshot live.
        if (_openItemContainerByPlayer.TryGetValue(netId, out var session) && session.PlaceId == placeId) {
            int sid = _nextEntityId++;
            session.ItemsByEntityId[sid] = new ContainerSessionItem {
                EntityId = sid, ConfigId = configId, SlotIndex = freeSlot, ItemUuid = itemUuid, Version = version,
            };
            session.SlotToEntityId[freeSlot] = sid;
            if (!string.IsNullOrEmpty(itemUuid))
                _bridges[sid] = new ItemDbBridge { Uuid = itemUuid, Version = version, PlaceId = placeId };
            SendItemContainerSnapshot(conn, session);
        }

        GameLogger.Network.Info("ItemIntoPackage configId={C} slot={S} netId={N}", configId, freeSlot, netId);
        onSuccess?.Invoke();
    }

    /// <summary>
    /// Place-monde où persister un item ToPersist lâché au sol : la place de
    /// l'appartement courant si le joueur y est, sinon la place "city" pour la
    /// rue/entrepôt. null si aucune (la ligne DB est alors conservée telle quelle).
    /// </summary>
    private string ResolveWorldPlaceId(NetworkConnectionToClient conn, string roomId)
    {
        ApartmentController apt = PropInteractionRouter.FindApartmentByConn(conn);
        if (!string.IsNullOrEmpty(apt?.HomeData?.Id)) return apt.HomeData.Id;
        if (roomId == "city") return ApiManager.Instance?.CityPlaceId;
        return null;
    }

    public void HandleDrop(NetworkConnectionToClient conn, C2S_RequestDropItem msg)
    {
        if (conn.identity == null) return;
        uint playerNetId = conn.identity.netId;
        string roomId    = PlayerRoomTracker.Instance.GetRoom(conn);

        GameLogger.Network.Debug("Item DropRequest player={PlayerNetId} hand={Hand}", playerNetId, msg.Hand);

        if (roomId == null)
        {
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = "Pas dans une pièce" });
            return;
        }

        if (!_playerHands.TryGetValue(playerNetId, out var handState))
        {
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = "Aucun objet en main" });
            return;
        }

        int entityId = handState.GetEntityId(msg.Hand);
        if (entityId == -1)
        {
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = "Main vide" });
            return;
        }

        if (!TryGetEntity(roomId, entityId, out var entity))
        {
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = "Objet introuvable" });
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

        // Persistance au drop, pilotée par ItemConfig.ToPersist (UNIFORME — aucun cas
        // spécial colis) :
        //  - ToPersist → l'item devient un item-monde PERSISTANT : on PATCH sa place vers la
        //    place de la pièce + sa position (UUID conservé → survit au redémarrage et se
        //    recharge au sol via ApartmentController). C'est ce qui préserve aussi la place
        //    "item_container:{uuid}" d'un colis et son contenu.
        //  - sinon → on supprime sa ligne DB (item-monde éphémère, comportement historique).
        ItemConfig dropCfg = DatabaseManager.GetItemConfigById(entity.ItemConfigId);
        if (dropCfg != null && dropCfg.ToPersist) {
            // Place-monde résolue selon le lieu : place de l'appartement si on y est,
            // sinon la place "city" (anchor de persistance de la rue/entrepôt). Le PATCH
            // conserve l'UUID + écrit la position → rechargé au sol au prochain boot.
            string worldPlaceId = ResolveWorldPlaceId(conn, roomId);
            if (!string.IsNullOrEmpty(worldPlaceId) && GetBridge(entityId) != null)
                PersistMoveToWorldAsync(entityId, worldPlaceId, dropPos, entity.Rotation);
            // Pas de place-monde résolue ou pas de ligne DB : on conserve la ligne telle
            // quelle (jamais de delete) — l'UUID survit.
        }
        else if (entity.Persistent) {
            PersistDropAsync(entityId);
        }

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

        // Un item-conteneur (« package ») doit être PERSISTÉ (UUID DB) pour porter sa
        // place "item_container:{uuid}" → on le spawn directement en main (DB-backed),
        // pas en item monde éphémère.
        ItemConfig cfg = DatabaseManager.GetItemConfigById(msg.ItemConfigId);
        if (cfg != null && cfg.Container != null && cfg.Container.IsContainer) {
            int handEntity = SpawnItemInHand(roomId, msg.ItemConfigId, conn, cfg, persistent: true);
            GameLogger.Network.Info("Package AdminSpawn entity={EntityId} configId={ItemConfigId} room={RoomId} conn={ConnId}",
                handEntity, msg.ItemConfigId, roomId, conn.connectionId);
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
        CloseAllSessions(netId);
        // Idem pour la session poche : libère les entityId+bridges alloués au connect.
        CleanupPocketSession(netId);

        // Filet de sécurité : despawn TOUT item WORLD encore en scène dont le verrou
        // propriétaire correspond à ce joueur (AuthorizedNetId == netId). Cas typique :
        // colis de Sort spawnés au pickup et non encore ramassés au moment du crash.
        // En temps normal, SortItemsStepInstance.OnExit s'en charge via la chaîne
        // MissionServerManager.OnPlayerDisconnected → FinalizeFail → step.OnExit ;
        // ce filet couvre les races où cette chaîne ne parvient pas à boucler.
        DespawnOrphanWorldItemsAuthorizedBy(netId);
    }

    /// <summary>
    /// Despawn tous les items WORLD (non tenus) à travers toutes les rooms dont
    /// le verrou <see cref="ItemEntity.AuthorizedNetId"/> correspond à <paramref name="netId"/>.
    /// Idempotent : si la chaîne MissionServerManager a déjà nettoyé un item, le
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
            // Bidon : pousse le niveau de carburant au client qui rejoint (tooltip).
            if (entity is FuelCanisterEntity fce)
                conn.Send(new S2C_ItemFuel { EntityId = entity.EntityId, Fuel = fce.Fuel });
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

    private HandType? ResolveHand(ItemConfig config, uint netId)
    {
        var state = GetOrCreateHandState(netId);
        if (config == null) return FreeHandRespectingTwoHand(state);

        if (config.HandleType == ItemHandleType.TWO_HAND)
            return (state.RightEntityId == -1 && state.LeftEntityId == -1) ? HandType.Right : (HandType?)null;

        return FreeHandRespectingTwoHand(state);
    }

    /// <summary>Tenir un item 2 mains bloque les DEUX mains, même s'il n'occupe qu'un slot
    /// logique → aucun item 1 main ne peut être pris en main tant qu'on tient le 2 mains.</summary>
    private HandType? FreeHandRespectingTwoHand(PlayerHandState state)
    {
        if (EntityIsTwoHand(state.RightEntityId) || EntityIsTwoHand(state.LeftEntityId)) return null;
        return state.GetFreeHand();
    }

    private bool EntityIsTwoHand(int entityId)
    {
        if (entityId == -1) return false;
        foreach (var room in _rooms.Values)
            if (room.TryGetValue(entityId, out var e)) {
                var cfg = DatabaseManager.GetItemConfigById(e.ItemConfigId);
                return cfg != null && cfg.HandleType == ItemHandleType.TWO_HAND;
            }
        return false;
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
        return ResolveHand(config, playerNetId).HasValue;
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

    /// <summary>Supprime en DB un item orphelin (config inexistante) repéré au restore des mains.
    /// Pas de bridge/entité runtime associé — on supprime directement par UUID (cascade backend).</summary>
    private IEnumerator DeleteOrphanItemCoroutine(string itemUuid) {
        UnityWebRequest req = ApiManager.Instance.DeleteItemRequest(itemUuid);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300)
            Debug.LogWarning($"[ServerItemManager] Delete orphan item {itemUuid} failed code={req.responseCode} body={req.downloadHandler?.text}");
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

    // PATCH d'un item persistant vers la place d'une pièce + sa position (item-monde au sol).
    // Préserve l'UUID + le bridge ; utilisé au drop d'un item ToPersist.
    private void PersistMoveToWorldAsync(int entityId, string placeId, Vector3 position, Quaternion rotation) {
        if (ApiManager.Instance == null) return;
        ItemDbBridge bridge = GetBridge(entityId);
        if (bridge == null) return;
        ApiManager.Instance.StartCoroutine(MoveItemToWorldCoroutine(entityId, bridge, placeId, position, rotation));
    }

    private IEnumerator MoveItemToWorldCoroutine(int entityId, ItemDbBridge bridge, string placeId,
        Vector3 position, Quaternion rotation) {
        UpdateItemBody body = new UpdateItemBody {
            expectedVersion = bridge.Version,
            placeId  = placeId,
            position = new Vector3Body(position),
            rotation = new Vector3Body(rotation.eulerAngles),
        };
        UnityWebRequest req = ApiManager.Instance.UpdateItemRequest(bridge.Uuid, body);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] Move item to world {bridge.Uuid} failed code={req.responseCode} body={req.downloadHandler?.text}");
            yield break;
        }
        try {
            ItemJson item = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text);
            if (item != null) UpdateBridgePlace(entityId, placeId, item.version);
        } catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] Move-to-world parse error: {e.Message}");
        }
    }

    /// <summary>
    /// On first room entry after connect, fetch the player's hand_left and
    /// hand_right place state and re-create runtime entities for each item
    /// found, attached to the corresponding hand. Items dropped on map during
    /// previous session are NOT restored (they were ephemeral).
    /// </summary>
    // Re-entrancy guard. A reconnect can fire OnPlayerEnterRoom twice, launching two
    // RestoreHandItemsCoroutine in parallel for the same player. Because RestoreOneHand
    // awaits a GET /places/state before spawning, both passes clear the dedup
    // (DespawnHeldRuntimeForRestore) BEFORE either spawns, then each spawns a runtime
    // entity for the same DB hand item → an orphan duplicate lingers in _rooms with a
    // stale position, no longer referenced by _playerHands. That orphan later breaks
    // "open held colis" (held=false → distance check on a stale position → "Trop loin").
    private readonly HashSet<uint> _restoringHands = new HashSet<uint>();

    private IEnumerator RestoreHandItemsCoroutine(NetworkConnectionToClient conn, string roomId, PlayerInventory inv) {
        if (conn?.identity == null) yield break;
        uint playerNetId = conn.identity.netId;

        // Skip if a restore for this player is already in flight (see _restoringHands).
        if (!_restoringHands.Add(playerNetId)) {
            GameLogger.Network.Debug("Item RestoreHands skip (already running) player={PlayerNetId} room={RoomId}",
                playerNetId, roomId);
            yield break;
        }

        try {
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
        } finally {
            _restoringHands.Remove(playerNetId);
        }
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

        // Le joueur a pu être TÉLÉPORTÉ (ex. vers son appartement) pendant la lecture DB async :
        // on (re)résout sa room ACTUELLE pour créer l'item tenu au bon endroit ET diffuser là où
        // il est réellement. Sinon l'item est créé dans une room déjà quittée (broadcast à 0
        // destinataire → item invisible) et le runtime serveur reste orphelin dans l'ancienne room.
        string liveRoom = PlayerRoomTracker.Instance.GetRoom(conn) ?? roomId;

        foreach (ItemJson it in state.items) {
            // Orphan guard : un item dont l'ItemConfig n'existe plus dans le projet (config
            // supprimée/renommée) ne peut JAMAIS être rendu ni manipulé côté client
            // (S2C_SpawnItem → "No prefab for itemConfigId=X"), mais il occuperait quand même
            // la main côté serveur → mains bloquées "pleines" alors qu'elles paraissent vides.
            // On le saute (la main reste libre) et on supprime la ligne morte pour auto-réparer.
            if (DatabaseManager.GetItemConfigById(it.configId) == null) {
                Debug.LogWarning($"[ServerItemManager] RestoreHand({hand}) orphan item config={it.configId} uuid={it.Id} — skip + delete dead row");
                if (!string.IsNullOrEmpty(it.Id))
                    ApiManager.Instance.StartCoroutine(DeleteOrphanItemCoroutine(it.Id));
                continue;
            }

            // Re-create the runtime entity attached to the player in the current room.
            int entityId = _nextEntityId++;
            var entity = new ItemEntity {
                EntityId      = entityId,
                RoomId        = liveRoom,
                ItemConfigId  = it.configId,
                Position      = conn.identity.transform.position,
                Rotation      = Quaternion.identity,
                HolderNetId   = playerNetId,
                HolderHand    = hand,
                LocalPosition = Vector3.zero,
                LocalRotation = Quaternion.identity,
            };

            if (!_rooms.TryGetValue(liveRoom, out var roomItems)) {
                roomItems = new Dictionary<int, ItemEntity>();
                _rooms[liveRoom] = roomItems;
            }
            roomItems[entityId] = entity;

            var handState = GetOrCreateHandState(playerNetId);
            handState.Set(hand, entityId);
            _playerHands[playerNetId] = handState;

            AssociateUuid(entityId, it.Id, it.version, handPlaceId);

            var spawnMsg = new S2C_SpawnItem {
                EntityId      = entityId,
                RoomId        = liveRoom,
                ItemConfigId  = it.configId,
                Position      = entity.Position,
                Rotation      = entity.Rotation,
                IsHeld        = true,
                HolderNetId   = playerNetId,
                HolderHand    = hand,
                LocalPosition = Vector3.zero,
                LocalRotation = Quaternion.identity,
            };

            // L'item TENU doit atteindre son propriétaire même si sa connexion n'est pas (encore)
            // listée dans la room au moment de ce broadcast asynchrone — timing de reconnexion :
            // la restauration fait une requête DB avant de diffuser, et GetConnectionsInRoom peut
            // alors renvoyer 0 (le joueur restaurait dans le vide → item invisible, mains pleines).
            // On l'envoie donc DIRECTEMENT à conn, plus le broadcast room pour les autres
            // observateurs (le garde anti-doublon de OnSpawnItem gère l'éventuel double envoi).
            if (conn != null && conn.isReady) conn.Send(spawnMsg);
            BroadcastToRoom(liveRoom, spawnMsg);

            GameLogger.Network.Info("Item Restore entity={EntityId} configId={ItemConfigId} player={PlayerNetId} hand={Hand} room={Room}",
                entityId, it.configId, playerNetId, hand, liveRoom);
        }
    }

    // ── Container handlers ────────────────────────────────────────────────────

    // Garde l'enum pour le typage de IPlaceContext.Kind (lecture côté code orchestrateur).
    // La classification placeId → PlaceKind et la résolution placeKey → UUID backend sont
    // désormais portées par ResolvePlace + les IPlaceContext concrets.
    private enum PlaceKind { Unknown, HandLeft, HandRight, Container, Pocket, ItemContainer }

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

        // Un seul conteneur de PROP ouvert à la fois (un item-conteneur tenu peut
        // rester ouvert simultanément → drag&drop entre les deux).
        ClosePropSession(netId);

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

    /// <summary>Ouvre le COFFRE d'un véhicule en réutilisant exactement le flux des conteneurs de
    /// prop (place "container:{uuid}", session, ContainerUI). <paramref name="vehicleUuid"/> = id
    /// persistant du véhicule, <paramref name="trunkCfg"/> = VehicleConfig.trunk. La validation
    /// d'accès/proximité est faite côté véhicule (CmdOpenTrunk) avant l'appel.</summary>
    public void OpenVehicleTrunk(NetworkConnectionToClient conn, string vehicleUuid, ContainerConfig trunkCfg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        if (trunkCfg == null || !trunkCfg.IsContainer) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Pas de coffre" });
            return;
        }
        if (string.IsNullOrEmpty(vehicleUuid)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Véhicule non persisté" });
            return;
        }
        string charId = conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Sans caractère" });
            return;
        }
        ClosePropSession(netId); // un seul conteneur de "prop"/coffre ouvert à la fois
        ApiManager.Instance?.StartCoroutine(
            OpenContainerCoroutine(conn, netId, 0, vehicleUuid, charId, trunkCfg,
                broadcastVisual: false, isVehicleTrunk: true));
    }

    private IEnumerator OpenContainerCoroutine(NetworkConnectionToClient conn, uint netId, int propId,
        string propUuid, string charId, ContainerConfig containerCfg, bool broadcastVisual = true,
        bool isVehicleTrunk = false)
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
            IsVehicleTrunk = isVehicleTrunk,
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
        // Ignoré pour le coffre de véhicule (pas de prop à animer).
        if (broadcastVisual && !string.IsNullOrEmpty(roomId)) {
            BroadcastToRoom(roomId, new S2C_ContainerVisualState {
                RoomId = roomId, PropId = propId, IsOpen = true,
            });
        }

        // Coffre de véhicule : pas de prop, le VehicleController joue le son via l'événement.
        if (isVehicleTrunk) OnVehicleTrunkStateChanged?.Invoke(propUuid, true);

        GameLogger.Network.Info("ContainerOpened propId={PropId} placeId={PlaceId} items={Count} netId={NetId}",
            propId, placeId, snapshot.Count, netId);
    }

    public void HandleOpenItemContainer(NetworkConnectionToClient conn, C2S_OpenItemContainer msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;

        // Un seul item-conteneur ouvert à la fois (un meuble peut rester ouvert en plus).
        CloseItemSession(netId);

        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (!TryGetEntity(roomId, msg.EntityId, out var entity)) {
            conn.Send(new S2C_ItemContainerOpenFailed { EntityId = msg.EntityId, ErrorMessage = "Item introuvable" });
            return;
        }

        // Ouvrable si tenu en main par CE joueur OU posé au sol à portée — pas besoin de
        // tenir le colis pour l'ouvrir. On considère l'entité tenue via DEUX sources : la map
        // _playerHands ET le HolderNetId porté par l'entité elle-même (mis à 0 au drop). La
        // seconde reste fiable même si _playerHands a été momentanément désynchronisé (ex.
        // restauration concurrente après reconnexion), ce qui évite un faux "Trop loin".
        bool held = entity.HolderNetId == netId
            || (_playerHands.TryGetValue(netId, out var hands)
                && (hands.LeftEntityId == msg.EntityId || hands.RightEntityId == msg.EntityId));
        if (!held && Vector3.Distance(conn.identity.transform.position, entity.Position) > 3f) {
            conn.Send(new S2C_ItemContainerOpenFailed { EntityId = msg.EntityId, ErrorMessage = "Trop loin" });
            return;
        }
        var itemCfg = DatabaseManager.GetItemConfigById(entity.ItemConfigId);
        if (itemCfg == null || itemCfg.Container == null || !itemCfg.Container.IsContainer) {
            conn.Send(new S2C_ItemContainerOpenFailed { EntityId = msg.EntityId, ErrorMessage = "Pas un conteneur" });
            return;
        }

        // Le package doit être persisté (UUID DB) — sa place est "item_container:{uuid}".
        var bridge = GetBridge(msg.EntityId);
        if (bridge == null || string.IsNullOrEmpty(bridge.Uuid)) {
            conn.Send(new S2C_ItemContainerOpenFailed { EntityId = msg.EntityId, ErrorMessage = "Package non persisté (UUID manquant)" });
            return;
        }

        ApiManager.Instance?.StartCoroutine(
            OpenItemContainerCoroutine(conn, netId, msg.EntityId, bridge.Uuid, itemCfg.Container));
    }

    private IEnumerator OpenItemContainerCoroutine(NetworkConnectionToClient conn, uint netId,
        int packageEntityId, string itemUuid, ContainerConfig containerCfg)
    {
        // 1. Place idempotente (POST /places) — owner = uuid de l'item package.
        string placeKey = $"item_container:{itemUuid}";
        var createBody = new CreatePlaceBody {
            placeKey = placeKey,
            type = "container",
            ownerId = itemUuid,
            properties = new Dictionary<string, object> {
                { "slotCount",     containerCfg.SlotCount },
                { "acceptedTypes", containerCfg.AcceptedTypes.Select(t => (int)t).ToArray() },
            },
        };
        UnityWebRequest createReq = ApiManager.Instance.CreatePlaceRequest(createBody);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode < 200 || createReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] OpenItemContainer create place failed code={createReq.responseCode}");
            conn.Send(new S2C_ItemContainerOpenFailed { EntityId = packageEntityId, ErrorMessage = "Erreur place" });
            yield break;
        }
        PlaceJson placeData = null;
        try { placeData = JsonConvert.DeserializeObject<PlaceJson>(createReq.downloadHandler.text); }
        catch (System.Exception e) { Debug.LogWarning($"[ServerItemManager] OpenItemContainer parse: {e.Message}"); }
        string placeId = placeData?.Id;
        if (string.IsNullOrEmpty(placeId)) {
            conn.Send(new S2C_ItemContainerOpenFailed { EntityId = packageEntityId, ErrorMessage = "Place sans id" });
            yield break;
        }

        // 2. State fetch.
        UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
        yield return stateReq.SendWebRequest();
        if (stateReq.responseCode != 200) {
            conn.Send(new S2C_ItemContainerOpenFailed { EntityId = packageEntityId, ErrorMessage = "Erreur fetch state" });
            yield break;
        }
        PlaceStateJson stateData = null;
        try { stateData = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text); }
        catch (System.Exception e) { Debug.LogWarning($"[ServerItemManager] OpenItemContainer state parse: {e.Message}"); }

        // 3. Alloue entityId session + bridges + snapshot.
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        var session = new ItemContainerSession {
            PackageEntityId = packageEntityId, PackageItemUuid = itemUuid,
            PlaceId = placeId, RoomId = roomId, OpenedBy = netId, Config = containerCfg,
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
        // Meubles emballés : tout prop présent dans la place du colis est un prop stocké
        // (la place item_container n'accueille QUE des props emballés). Entrée affichée avec
        // l'icône réelle (PropConfigId), non-draggable cette itération (pas de bridge items).
        if (stateData?.props != null) {
            foreach (var p in stateData.props) {
                int slotIndex = ReadSlotIndex(p.stateData);
                int entityId = _nextEntityId++;
                session.ItemsByEntityId[entityId] = new ContainerSessionItem {
                    EntityId = entityId, ConfigId = 0, SlotIndex = slotIndex,
                    ItemUuid = p.Id, Version = p.version, IsProp = true,
                    PropConfigId = p.configId, PropPresetId = p.presetIndex,
                };
                if (!session.SlotToEntityId.ContainsKey(slotIndex)) session.SlotToEntityId[slotIndex] = entityId;
                snapshot.Add(new S2C_ContainerItem {
                    EntityId = entityId, ConfigId = 0, SlotIndex = slotIndex,
                    PropConfigId = p.configId, PropPresetId = p.presetIndex,
                });
            }
        }
        _openItemContainerByPlayer[netId] = session;

        conn.Send(new S2C_ItemContainerOpened {
            EntityId = packageEntityId,
            PlaceId = placeId,
            SlotCount = containerCfg.SlotCount,
            AcceptedTypes = containerCfg.AcceptedTypes.Select(t => (byte)t).ToArray(),
            Items = snapshot.ToArray(),
        });

        GameLogger.Network.Info("ItemContainerOpened entityId={EntityId} placeId={PlaceId} items={Count} netId={NetId}",
            packageEntityId, placeId, snapshot.Count, netId);
    }

    // ── Emballage d'un prop dans le colis (identité préservée) ─────────────────
    // Le prop n'est PAS supprimé : sa ligne `props` (et son UUID) est DÉPLACÉE dans la
    // place du colis (place_id = item_container:{uuid}, is_built=false, stateData.slotIndex).
    // Seul le runtime quitte le monde (RemoveProp → broadcast S2C_PropRemove). Le déballage
    // futur = un simple PATCH retour vers le monde (aucune création). Marche que le colis
    // soit ouvert ou non. Tout meuble présent dans la place du colis = meuble emballé.

    public void HandlePackProp(NetworkConnectionToClient conn, C2S_PackProp msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        var player = conn.identity.GetComponent<Sim.PlayerController>();
        string charId = player?.CharacterData?.Id;
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (string.IsNullOrEmpty(charId) || string.IsNullOrEmpty(roomId)) return;

        // 1. Colis tenu qui accepte les meubles.
        if (!TryGetHeldPackage(netId, roomId, out _, out string pkgUuid, out ContainerConfig pkgCfg)) {
            player?.TargetActionFailed(conn, "Aucun colis en main");
            return;
        }
        if (!pkgCfg.AcceptsProps) {
            player?.TargetActionFailed(conn, "Ce colis n'accepte pas les meubles");
            return;
        }

        // 2. Meuble possédé + adossé DB (UUID requis pour le PATCH /props).
        if (!ServerPropManager.Instance.TryGetPropState(roomId, msg.PropId, out var propState)) {
            player?.TargetActionFailed(conn, "Meuble introuvable");
            return;
        }
        // Un meuble-conteneur ne peut être emballé que s'il est VIDE → vérifié en async dans
        // PackPropCoroutine (lecture de sa place "container:{uuid}").
        if (propState.OwnerCharId != charId) {
            player?.TargetActionFailed(conn, "Ce meuble ne vous appartient pas");
            return;
        }
        var propBridge = ServerPropManager.Instance.GetBridge(msg.PropId);
        if (propBridge == null || string.IsNullOrEmpty(propBridge.Uuid)) {
            player?.TargetActionFailed(conn, "Meuble non emballable");
            return;
        }

        int presetId = PropStateHeader.ReadFrom(propState.Payload).PresetId;
        ApiManager.Instance?.StartCoroutine(PackPropCoroutine(
            conn, netId, msg.PropId, propBridge.Uuid, propBridge.Version, propState.PrefabId, presetId, pkgUuid, pkgCfg, roomId));
    }

    private IEnumerator PackPropCoroutine(NetworkConnectionToClient conn, uint netId, int propId,
        string propUuid, int propVersion, int propConfigId, int presetId, string pkgUuid, ContainerConfig pkgCfg, string roomId)
    {
        var player = conn.identity != null ? conn.identity.GetComponent<Sim.PlayerController>() : null;

        // 0. Si le meuble est lui-même un conteneur, il ne peut être emballé que VIDE.
        var packPropCfg = DatabaseManager.GetPropsById(propConfigId);
        if (packPropCfg != null && packPropCfg.Container != null && packPropCfg.Container.IsContainer) {
            bool emptyProp = true;
            yield return CheckContainerEmptyByUuid(propUuid, isProp: true, e => emptyProp = e);
            if (!emptyProp) { player?.TargetActionFailed(conn, InventoryToasts.NestedStorageMustBeEmpty); yield break; }
        }

        // 1. Place du colis (POST idempotent).
        string placeKey = $"item_container:{pkgUuid}";
        var createBody = new CreatePlaceBody {
            placeKey = placeKey, type = "container", ownerId = pkgUuid,
            properties = new Dictionary<string, object> {
                { "slotCount",     pkgCfg.SlotCount },
                { "acceptedTypes", pkgCfg.AcceptedTypes.Select(t => (int)t).ToArray() },
            },
        };
        UnityWebRequest createReq = ApiManager.Instance.CreatePlaceRequest(createBody);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode < 200 || createReq.responseCode >= 300) { player?.TargetActionFailed(conn, "Erreur colis"); yield break; }
        PlaceJson placeData = null;
        try { placeData = JsonConvert.DeserializeObject<PlaceJson>(createReq.downloadHandler.text); } catch { }
        string placeId = placeData?.Id;
        if (string.IsNullOrEmpty(placeId)) { player?.TargetActionFailed(conn, "Colis sans place"); yield break; }

        // 2. Slot libre = index non occupé par un item NI un meuble déjà stocké.
        UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
        yield return stateReq.SendWebRequest();
        var occupied = new HashSet<int>();
        if (stateReq.responseCode == 200) {
            try {
                var sd = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text);
                if (sd?.items != null) foreach (var it in sd.items) occupied.Add(ReadSlotIndex(it.stateData));
                if (sd?.props != null) foreach (var p in sd.props) occupied.Add(ReadSlotIndex(p.stateData));
            } catch { }
        }
        int freeSlot = -1;
        for (int i = 0; i < pkgCfg.SlotCount; i++) if (!occupied.Contains(i)) { freeSlot = i; break; }
        if (freeSlot < 0) { player?.TargetActionFailed(conn, "Colis plein"); yield break; } // meuble NON retiré du monde

        // 3. Déplace le meuble dans la place du colis (UUID conservé). Les meubles à-construire
        // (toBuild) repassent à-construire ; ceux construits de base le restent (jamais de phase
        // de construction → is_built doit rester true même stocké).
        var packCfg = DatabaseManager.GetPropsById(propConfigId);
        bool packBuilt = packCfg != null && !packCfg.MustBeBuilt();
        var body = new UpdatePropBody {
            expectedVersion = propVersion,
            placeId   = placeId,
            isBuilt   = packBuilt,
            stateData = new Dictionary<string, object> { { "slotIndex", freeSlot } },
        };
        UnityWebRequest patchReq = ApiManager.Instance.UpdatePropRequest(propUuid, body);
        yield return patchReq.SendWebRequest();
        if (patchReq.responseCode < 200 || patchReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] PackProp PATCH /props failed code={patchReq.responseCode} body={patchReq.downloadHandler?.text}");
            player?.TargetActionFailed(conn, "Échec emballage");
            yield break; // le meuble reste dans le monde
        }
        int newVersion = propVersion + 1;
        try { var pj = JsonConvert.DeserializeObject<PropJson>(patchReq.downloadHandler.text); if (pj != null) newVersion = pj.version; } catch { }

        // Feedback : VFX d'emballage à la position du meuble (diffusé à toute la room),
        // capturé AVANT le retrait runtime. Le toast de succès part au seul emballeur.
        GameObject packedGo = ServerPropManager.Instance.GetSpawnedGameObject(propId);
        if (packedGo != null) {
            BroadcastToRoom(roomId, new S2C_PropPacked { RoomId = roomId, Position = packedGo.transform.position });
        }

        // 4. Retire le meuble du monde (runtime + broadcast S2C_PropRemove). PAS de DELETE DB.
        ServerPropManager.Instance.RemoveProp(roomId, propId);

        if (conn != null && conn.isReady) {
            conn.Send(new ToastNotificationMessage {
                text       = "Meuble emballé",
                typeByte   = (byte)NotificationType.JOB,
                worldToast = true,
                kindByte   = (byte)ToastKind.Success,
            });
        }

        // 5. Si le colis est ouvert : ajoute l'entrée meuble + snapshot live.
        if (_openItemContainerByPlayer.TryGetValue(netId, out var session) && session.PlaceId == placeId) {
            int entityId = _nextEntityId++;
            session.ItemsByEntityId[entityId] = new ContainerSessionItem {
                EntityId = entityId, ConfigId = 0, SlotIndex = freeSlot,
                ItemUuid = propUuid, Version = newVersion, IsProp = true,
                PropConfigId = propConfigId, PropPresetId = presetId,
            };
            session.SlotToEntityId[freeSlot] = entityId;
            SendItemContainerSnapshot(conn, session);
        }

        GameLogger.Network.Info("PropPacked propUuid={Uuid} propConfigId={ConfigId} slot={Slot} netId={NetId}",
            propUuid, propConfigId, freeSlot, netId);
    }

    // ── Déballage : le meuble emballé retourne dans le monde (UUID conservé) ───────
    // Résout le colis par le bridge de PackageEntityId (tenu OU au sol), retrouve le
    // meuble au SlotIndex dans la place du colis, PATCH sa ligne `props` vers la place
    // de l'appartement + position (is_built=false → à-construire), re-spawn le runtime
    // avec le MÊME UUID. Build mode côté client (comme la delivery box).

    public void HandleUnpackProp(NetworkConnectionToClient conn, C2S_UnpackProp msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        var player = conn.identity.GetComponent<Sim.PlayerController>();
        string charId = player?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) return;

        var pkgBridge = GetBridge(msg.PackageEntityId);
        if (pkgBridge == null || string.IsNullOrEmpty(pkgBridge.Uuid)) {
            player?.TargetActionFailed(conn, "Colis introuvable");
            return;
        }
        ApartmentController apt = PropInteractionRouter.FindApartmentByConn(conn);
        if (apt == null || string.IsNullOrEmpty(apt.HomeData?.Id)) {
            player?.TargetActionFailed(conn, "Déballage possible seulement dans un appartement");
            return;
        }

        ApiManager.Instance?.StartCoroutine(UnpackPropCoroutine(
            conn, netId, pkgBridge.Uuid, msg.SlotIndex, apt, charId, msg.Position, msg.Rotation));
    }

    private IEnumerator UnpackPropCoroutine(NetworkConnectionToClient conn, uint netId, string pkgUuid,
        int slotIndex, ApartmentController apt, string charId, Vector3 position, Quaternion rotation)
    {
        var player = conn.identity != null ? conn.identity.GetComponent<Sim.PlayerController>() : null;

        // 1. Place du colis (déjà existante) → retrouver le meuble au slot demandé.
        var createBody = new CreatePlaceBody { placeKey = $"item_container:{pkgUuid}", type = "container", ownerId = pkgUuid };
        UnityWebRequest createReq = ApiManager.Instance.CreatePlaceRequest(createBody);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode < 200 || createReq.responseCode >= 300) { player?.TargetActionFailed(conn, "Erreur colis"); yield break; }
        PlaceJson placeData = null;
        try { placeData = JsonConvert.DeserializeObject<PlaceJson>(createReq.downloadHandler.text); } catch { }
        string placeId = placeData?.Id;
        if (string.IsNullOrEmpty(placeId)) { player?.TargetActionFailed(conn, "Colis sans place"); yield break; }

        UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
        yield return stateReq.SendWebRequest();
        PropJson packed = null;
        if (stateReq.responseCode == 200) {
            try {
                var sd = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text);
                if (sd?.props != null) foreach (var p in sd.props) if (ReadSlotIndex(p.stateData) == slotIndex) { packed = p; break; }
            } catch { }
        }
        if (packed == null || string.IsNullOrEmpty(packed.Id)) { player?.TargetActionFailed(conn, "Meuble introuvable"); yield break; }

        // 2. PATCH le meuble vers l'appartement + position (UUID conservé). Les meubles à-construire
        // (toBuild) ressortent à-construire ; ceux construits de base restent construits.
        var unpackCfg = DatabaseManager.GetPropsById(packed.configId);
        bool unpackBuilt = unpackCfg != null && !unpackCfg.MustBeBuilt();
        var body = new UpdatePropBody {
            expectedVersion = packed.version,
            placeId   = apt.HomeData.Id,
            position  = new Vector3Body(position),
            rotation  = new Vector3Body(rotation.eulerAngles),
            isBuilt   = unpackBuilt,
            stateData = new Dictionary<string, object>(), // libère le slotIndex
        };
        UnityWebRequest patchReq = ApiManager.Instance.UpdatePropRequest(packed.Id, body);
        yield return patchReq.SendWebRequest();
        if (patchReq.responseCode < 200 || patchReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] UnpackProp PATCH /props failed code={patchReq.responseCode} body={patchReq.downloadHandler?.text}");
            player?.TargetActionFailed(conn, "Échec déballage");
            yield break;
        }
        int newVersion = packed.version + 1;
        try { var pj = JsonConvert.DeserializeObject<PropJson>(patchReq.downloadHandler.text); if (pj != null) newVersion = pj.version; } catch { }

        // 3. Re-spawn runtime dans l'appart (UUID conservé ; built selon toBuild).
        var header = new PropStateHeader { IsBuilt = unpackBuilt, PresetId = packed.presetIndex };
        int newPropId = ServerPropManager.Instance.SpawnProp(
            apt.RoomId, packed.configId, position, rotation,
            headerOverride: header, propUuid: packed.Id, propVersion: newVersion, ownerCharId: charId);
        if (newPropId < 0) { player?.TargetActionFailed(conn, "Échec spawn meuble"); yield break; }
        apt.TrackProp(newPropId);
        GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(newPropId);
        if (go != null && apt.PropsContainer != null) {
            go.transform.SetParent(apt.PropsContainer);
            go.transform.position = position;
            go.transform.rotation = rotation;
        }

        // 4. Retire l'entrée du colis ouvert (si ouvert) + snapshot.
        if (_openItemContainerByPlayer.TryGetValue(netId, out var session) && session.PlaceId == placeId) {
            if (session.SlotToEntityId.TryGetValue(slotIndex, out int eid)) {
                session.ItemsByEntityId.Remove(eid);
                session.SlotToEntityId.Remove(slotIndex);
            }
            SendItemContainerSnapshot(conn, session);
        }

        GameLogger.Network.Info("PropUnpacked propUuid={Uuid} configId={Config} slot={Slot} room={Room} netId={NetId}",
            packed.Id, packed.configId, slotIndex, apt.RoomId, netId);
    }

    // ── Lâcher au sol un item rangé (colis tenu OU poche) ──────────────────────
    // L'item quitte sa place (colis/poche) et devient un item-monde au pied du joueur.
    // ToPersist → la ligne DB est déplacée vers la place du monde + position (UUID conservé) ;
    // sinon la ligne DB est supprimée (item-monde éphémère).
    public void HandleDropFromInventory(NetworkConnectionToClient conn, C2S_DropFromInventory msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null) return;
        var player = conn.identity.GetComponent<Sim.PlayerController>();

        string uuid = null; int configId = 0, version = 1, slotIndex = -1;
        ItemContainerSession contSession = null; PocketSession pocketSession = null;
        bool fromContainer = false, fromPocket = false;

        // NB : TryGetValue(out session) renseigne `session` même sur un miss du second test ;
        // on s'appuie donc sur les flags fromContainer/fromPocket, PAS sur (session != null).
        if (_openItemContainerByPlayer.TryGetValue(netId, out contSession)
            && contSession.ItemsByEntityId.TryGetValue(msg.EntityId, out var ci) && !ci.IsProp) {
            // Le colis doit être tenu en main (exigence : drop depuis un colis tenu).
            if (!TryGetHeldPackage(netId, roomId, out _, out string heldUuid, out _)
                || heldUuid != contSession.PackageItemUuid) {
                player?.TargetActionFailed(conn, "Le colis doit être en main");
                return;
            }
            uuid = ci.ItemUuid; configId = ci.ConfigId; version = ci.Version; slotIndex = ci.SlotIndex;
            fromContainer = true;
        }
        else if (_pocketByPlayer.TryGetValue(netId, out pocketSession)
            && pocketSession.ItemsByEntityId.TryGetValue(msg.EntityId, out var pi)) {
            uuid = pi.ItemUuid; configId = pi.ConfigId; version = pi.Version; slotIndex = pi.SlotIndex;
            fromPocket = true;
        }
        else return; // introuvable

        if (string.IsNullOrEmpty(uuid)) return;

        // Position de drop : devant le joueur, raycast au sol (layer 9).
        Vector3 dropPos = conn.identity.transform.position + conn.identity.transform.forward * 0.6f;
        if (Physics.Raycast(dropPos + Vector3.up, Vector3.down, out var hitRay, 5f, 1 << 9)) dropPos = hitRay.point;

        // Item-monde runtime au sol.
        int worldEntity = SpawnItem(roomId, configId, dropPos, Quaternion.identity);

        // Retire l'entrée de la session source + bridge + snapshot.
        if (fromContainer) {
            contSession.ItemsByEntityId.Remove(msg.EntityId);
            if (slotIndex >= 0) contSession.SlotToEntityId.Remove(slotIndex);
            ClearBridge(msg.EntityId);
            SendItemContainerSnapshot(conn, contSession);
        } else if (fromPocket) {
            pocketSession.ItemsByEntityId.Remove(msg.EntityId);
            if (slotIndex >= 0) pocketSession.SlotToEntityId.Remove(slotIndex);
            ClearBridge(msg.EntityId);
            SendPocketSnapshot(conn, pocketSession);
        }

        ItemConfig cfg = DatabaseManager.GetItemConfigById(configId);
        string worldPlaceId = ResolveWorldPlaceId(conn, roomId);
        bool toPersist = cfg != null && cfg.ToPersist && !string.IsNullOrEmpty(worldPlaceId);
        ApiManager.Instance?.StartCoroutine(
            DropFromInventoryCoroutine(worldEntity, uuid, version, worldPlaceId, dropPos, toPersist));

        GameLogger.Network.Info("DropFromInventory uuid={Uuid} configId={Config} toPersist={Persist} netId={NetId}",
            uuid, configId, toPersist, netId);
    }

    private IEnumerator DropFromInventoryCoroutine(int worldEntity, string uuid, int version,
        string worldPlaceId, Vector3 dropPos, bool toPersist) {
        if (toPersist) {
            var body = new UpdateItemBody {
                expectedVersion = version,
                placeId  = worldPlaceId,
                position = new Vector3Body(dropPos),
                rotation = new Vector3Body(Vector3.zero),
                stateData = new Dictionary<string, object>(), // libère le slotIndex
            };
            UnityWebRequest req = ApiManager.Instance.UpdateItemRequest(uuid, body);
            yield return req.SendWebRequest();
            if (req.responseCode >= 200 && req.responseCode < 300) {
                int nv = version + 1;
                try { var it = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text); if (it != null) nv = it.version; } catch { }
                AssociateUuid(worldEntity, uuid, nv, worldPlaceId); // bridge + Persistent=true
            } else {
                Debug.LogWarning($"[ServerItemManager] DropFromInventory PATCH failed code={req.responseCode} body={req.downloadHandler?.text}");
            }
        } else {
            UnityWebRequest req = ApiManager.Instance.DeleteItemRequest(uuid);
            yield return req.SendWebRequest();
        }
    }

    public void HandleCloseContainer(NetworkConnectionToClient conn, C2S_CloseContainer msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        // Chaque panneau client ferme SA propre session (prop OU item) — les deux peuvent
        // être ouvertes simultanément.
        if (msg.ItemContainer) CloseItemSession(netId);
        else                   ClosePropSession(netId);
    }

    /// <summary>Ferme la session conteneur de PROP du joueur (un meuble peut rester
    /// ouvert en même temps qu'un item-conteneur tenu).</summary>
    private void ClosePropSession(uint netId) {
        if (!_openContainerByPlayer.TryGetValue(netId, out var session)) return;
        _openContainerByPlayer.Remove(netId);
        // Libère les bridges session (les entityId alloués à la session ne sont
        // plus valides après close ; un re-open re-allouera des nouveaux ids).
        foreach (var it in session.ItemsByEntityId.Values) _bridges.Remove(it.EntityId);

        // Coffre de véhicule : pas de prop à animer → le VehicleController joue le son de fermeture.
        if (session.IsVehicleTrunk) OnVehicleTrunkStateChanged?.Invoke(session.PropUuid, false);

        // Broadcast visuel : porte/couvercle se referme — MAIS seulement si plus aucun autre
        // joueur n'a ce même meuble ouvert. Chaque joueur a sa propre session ; sans ce compte,
        // le premier à fermer refermait la porte alors que d'autres viewers étaient encore dessus.
        bool anotherViewer = false;
        foreach (var other in _openContainerByPlayer.Values) {
            if (other.PropId == session.PropId && other.RoomId == session.RoomId) { anotherViewer = true; break; }
        }

        // session.RoomId est figé au moment de l'open → fonctionne même si OpenedBy a quitté la room/déco.
        if (!anotherViewer && !string.IsNullOrEmpty(session.RoomId)) {
            BroadcastToRoom(session.RoomId, new S2C_ContainerVisualState {
                RoomId = session.RoomId, PropId = session.PropId, IsOpen = false,
            });
        }
        GameLogger.Network.Debug("ContainerClosed netId={NetId} placeId={PlaceId} keptOpen={Kept}",
            netId, session.PlaceId, anotherViewer);
    }

    /// <summary>Ferme la session item-conteneur (« colis ») du joueur. Pas de broadcast
    /// visuel (item tenu).</summary>
    private void CloseItemSession(uint netId) {
        if (!_openItemContainerByPlayer.TryGetValue(netId, out var itemSession)) return;
        _openItemContainerByPlayer.Remove(netId);
        foreach (var it in itemSession.ItemsByEntityId.Values) _bridges.Remove(it.EntityId);
        GameLogger.Network.Debug("ItemContainerClosed netId={NetId} placeId={PlaceId}", netId, itemSession.PlaceId);
    }

    /// <summary>Ferme les deux types de session (déconnexion, cleanup).</summary>
    private void CloseAllSessions(uint netId) {
        ClosePropSession(netId);
        CloseItemSession(netId);
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
        // Anti-boucle : un colis ne peut pas être rangé dans son propre conteneur.
        if (toCtx is ItemContainerPlaceContext icc
            && !string.IsNullOrEmpty(itemCtx.ItemUuid)
            && itemCtx.ItemUuid == icc.PackageItemUuid) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Un colis ne peut pas se contenir lui-même" });
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

        // Règle "conteneur imbriqué" : ranger un conteneur DANS un autre n'est permis que VIDE.
        bool allowNest = true;
        yield return GateNestedContainer(itemCtx, toCtx, ok => allowNest = ok);
        if (!allowNest) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId,
                ErrorMessage = InventoryToasts.NestedStorageMustBeEmpty });
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
        PropagateContainerUpdateToOtherSubscribers(conn.identity.netId, fromCtx, toCtx);
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

        // Anti-boucle : un colis ne peut pas atterrir dans son propre conteneur via un swap.
        // A → ctxB, B → ctxA : on rejette si l'item déplacé est le colis du conteneur cible.
        bool aIntoOwnPackage = ctxB is ItemContainerPlaceContext iccB
            && !string.IsNullOrEmpty(itemA.ItemUuid) && itemA.ItemUuid == iccB.PackageItemUuid;
        bool bIntoOwnPackage = ctxA is ItemContainerPlaceContext iccA
            && !string.IsNullOrEmpty(itemB.ItemUuid) && itemB.ItemUuid == iccA.PackageItemUuid;
        if (aIntoOwnPackage || bIntoOwnPackage) {
            int rejectedEntity = aIntoOwnPackage ? msg.EntityIdA : msg.EntityIdB;
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = rejectedEntity, ErrorMessage = "Un colis ne peut pas se contenir lui-même" });
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
        // Règle "conteneur imbriqué" : A va vers ctxB et B vers ctxA ; chacun n'est permis dans
        // un conteneur que s'il est VIDE. Vérifié avant toute écriture.
        bool okA = true, okB = true;
        yield return GateNestedContainer(itemA, ctxB, v => okA = v);
        yield return GateNestedContainer(itemB, ctxA, v => okB = v);
        if (!okA || !okB) {
            conn.Send(new S2C_MoveItemResult { Success = false,
                EntityId = !okA ? msg.EntityIdA : msg.EntityIdB,
                ErrorMessage = InventoryToasts.NestedStorageMustBeEmpty });
            PushSnapshotsFor(conn, ctxA, ctxB);
            yield break;
        }

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
        PropagateContainerUpdateToOtherSubscribers(conn.identity.netId, ctxA, ctxB);

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

    private static void SendItemContainerSnapshot(NetworkConnectionToClient conn, ItemContainerSession session)
    {
        var items = new List<S2C_ContainerItem>(session.ItemsByEntityId.Count);
        foreach (var it in session.ItemsByEntityId.Values) {
            items.Add(new S2C_ContainerItem {
                EntityId = it.EntityId, ConfigId = it.ConfigId, SlotIndex = it.SlotIndex,
                PropConfigId = it.IsProp ? it.PropConfigId : 0,
                PropPresetId = it.IsProp ? it.PropPresetId : 0,
            });
        }
        conn.Send(new S2C_ItemContainerOpened {
            EntityId      = session.PackageEntityId,
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
        /// <summary>Coffre de véhicule : autorise l'imbrication de conteneurs non vides.</summary>
        public bool      AllowsNesting => _session.Config != null && _session.Config.AllowsNestedContainers;

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
            // Imbrication de conteneur autorisée si le conteneur déplacé est vide → vérifiée en
            // async dans MoveItemCoroutine/SwapItemsCoroutine (GateNestedContainer), pas ici.
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

    private sealed class ItemContainerPlaceContext : IPlaceContext {
        private readonly ServerItemManager    _mgr;
        private readonly ItemContainerSession _session;

        public ItemContainerPlaceContext(ServerItemManager mgr, ItemContainerSession session) { _mgr = mgr; _session = session; }

        public PlaceKind Kind         => PlaceKind.ItemContainer;
        public string    PlaceId      => _session.PlaceId;
        public bool      HasSlotIndex => true;
        public string    PackageItemUuid => _session.PackageItemUuid; // pour le garde anti-auto-conteneur

        public bool TryResolveItem(int entityId, int declaredSlot, out ItemCtx ctx, out string err) {
            ctx = default; err = null;
            if (!_session.ItemsByEntityId.TryGetValue(entityId, out var si)) {
                err = "Item package introuvable"; return false;
            }
            // Un meuble emballé n'est pas déplaçable cette itération (déballage = futur).
            if (si.IsProp) {
                err = "Meuble emballé non déplaçable"; return false;
            }
            if (declaredSlot >= 0 && si.SlotIndex != declaredSlot) {
                err = "Slot package incohérent"; return false;
            }
            ctx = new ItemCtx { ConfigId = si.ConfigId, ItemUuid = si.ItemUuid, Version = si.Version };
            return true;
        }

        public bool ValidateAsTarget(int slotIndex, int configId, out string err) {
            err = null;
            var itemConfig = DatabaseManager.ItemConfigs.Find(x => x.ID == configId);
            // Imbrication de conteneur autorisée si le conteneur déplacé est vide → vérifiée en
            // async dans MoveItemCoroutine/SwapItemsCoroutine (GateNestedContainer), pas ici.
            if (itemConfig != null && !_session.Config.Accepts(itemConfig.Type)) {
                err = "Type refusé par ce package"; return false;
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
        if (_openItemContainerByPlayer.TryGetValue(netId, out var openItemContainer) && placeId == openItemContainer.PlaceId) {
            return new ItemContainerPlaceContext(this, openItemContainer);
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
        if ((from.Kind == PlaceKind.ItemContainer || to.Kind == PlaceKind.ItemContainer)
            && _openItemContainerByPlayer.TryGetValue(netId, out var itemContainer)) {
            SendItemContainerSnapshot(conn, itemContainer);
        }
    }

    /// <summary>
    /// Quand un joueur mute un conteneur, tous les AUTRES joueurs qui ont ce même
    /// PlaceId backend ouvert ont une session figée (entityId locaux, slot mapping)
    /// qui ne reflète plus la DB. On les re-synchronise : refetch de l'état canonique,
    /// reconstruction de leur session côté serveur, re-push d'un snapshot complet.
    /// </summary>
    private void PropagateContainerUpdateToOtherSubscribers(uint moverNetId, IPlaceContext from, IPlaceContext to) {
        string placeId = from.Kind == PlaceKind.Container ? from.PlaceId
                       : to.Kind   == PlaceKind.Container ? to.PlaceId
                       : null;
        if (string.IsNullOrEmpty(placeId) || ApiManager.Instance == null) return;

        List<uint> toRefresh = null;
        foreach (var kv in _openContainerByPlayer) {
            if (kv.Key == moverNetId) continue;
            if (kv.Value.PlaceId != placeId) continue;
            (toRefresh ??= new List<uint>()).Add(kv.Key);
        }
        if (toRefresh == null) return;

        foreach (uint netId in toRefresh) {
            if (!_openContainerByPlayer.TryGetValue(netId, out var session)) continue;
            if (!NetworkServer.spawned.TryGetValue(netId, out var identity)) continue;
            var conn = identity?.connectionToClient;
            if (conn == null) continue;
            ApiManager.Instance.StartCoroutine(RefreshContainerSessionAndPushSnapshot(conn, session));
        }
    }

    /// <summary>
    /// Refetch l'état d'une place conteneur depuis la DB et reconstruit la session
    /// in-memory de <paramref name="conn"/> (libère les anciens bridges, alloue de
    /// nouveaux entityId), puis pousse le snapshot client (<c>S2C_ContainerOpened</c>).
    /// </summary>
    private IEnumerator RefreshContainerSessionAndPushSnapshot(NetworkConnectionToClient conn, ContainerSession session) {
        UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(session.PlaceId);
        yield return stateReq.SendWebRequest();

        // Le joueur a pu fermer son panneau pendant le fetch : si la session n'est
        // plus active, on s'arrête là — sinon un S2C_ContainerOpened tardif rouvrirait
        // l'UI côté client.
        if (!_openContainerByPlayer.TryGetValue(session.OpenedBy, out var current) || current != session) {
            yield break;
        }

        if (stateReq.responseCode != 200) {
            Debug.LogWarning($"[ServerItemManager] RefreshContainerSession fetch failed code={stateReq.responseCode}");
            yield break;
        }
        PlaceStateJson stateData = null;
        try { stateData = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text); }
        catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] RefreshContainerSession parse error: {e.Message}");
            yield break;
        }

        // Libère les anciens bridges/entityId de cette session (chaque autre joueur
        // a ses propres entityId — on les recrée à partir de zéro).
        foreach (var it in session.ItemsByEntityId.Values) _bridges.Remove(it.EntityId);
        session.ItemsByEntityId.Clear();
        session.SlotToEntityId.Clear();

        if (stateData?.items != null) {
            foreach (var it in stateData.items) {
                int slotIndex = ReadSlotIndex(it.stateData);
                int entityId = _nextEntityId++;
                session.ItemsByEntityId[entityId] = new ContainerSessionItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex,
                    ItemUuid = it.Id, Version = it.version,
                };
                if (!session.SlotToEntityId.ContainsKey(slotIndex)) session.SlotToEntityId[slotIndex] = entityId;
                _bridges[entityId] = new ItemDbBridge {
                    Uuid = it.Id, Version = it.version, PlaceId = session.PlaceId,
                };
            }
        }

        SendContainerSnapshot(conn, session);
        GameLogger.Network.Debug("ContainerSession refreshed placeId={PlaceId} netId={NetId} items={Count}",
            session.PlaceId, session.OpenedBy, session.ItemsByEntityId.Count);
    }
}
