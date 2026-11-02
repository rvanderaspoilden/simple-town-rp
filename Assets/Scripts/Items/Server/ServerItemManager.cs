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
        public int    Quantity = 1;   // taille de la pile (≥1). Mute par split/merge ; broadcasté via S2C_ContainerItem.Quantity.
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
    // Chaque joueur a UNE poche persistée à la place "pocket:{charId}",
    // PocketCapacity slots. La session est créée par EnsurePocketSession au connect et libérée
    // à la déconnexion.
    private const int PocketCapacity = 8;

    private sealed class PocketSessionItem {
        public int    EntityId;
        public int    ConfigId;
        public int    SlotIndex;
        public int    Quantity = 1;   // invariant: 1 (poche = 1 item/slot, jamais de stack). Kept pour symétrie.
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

    // ── Projection main (hotbar, modèle pointeur) ────────────────────────
    // L'item équipé via la hotbar NE QUITTE PAS sa place poche : la main affiche une
    // entité « projection » dont le bridge est PARTAGÉ avec la ligne DB de l'item
    // (les PATCHes chargeur/carburant gardent une version OCC unique).
    private readonly Dictionary<uint, (int worldEntityId, int pocketEntityId)> _handProjection
        = new Dictionary<uint, (int, int)>();

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

        // Chargeur d'arme suivi sous la clé éphémère "e:{entityId}" (persist pickup
        // asynchrone) : migre vers l'uuid, stable pour le reste de la session serveur.
        if (!string.IsNullOrEmpty(uuid) && _weaponAmmoByKey.TryGetValue("e:" + entityId, out int loadedAmmo)) {
            _weaponAmmoByKey.Remove("e:" + entityId);
            _weaponAmmoByKey[uuid] = loadedAmmo;
        }

        // Montant de liasse suivi sous la clé éphémère "e:{entityId}" : migre vers l'uuid et
        // persiste MAINTENANT — contrairement au chargeur, le montant ne change plus jamais,
        // donc aucun « prochain Set » ne rattraperait une écriture manquée à la création.
        if (!string.IsNullOrEmpty(uuid) && _cashAmountByKey.TryGetValue("e:" + entityId, out int cashAmount)) {
            _cashAmountByKey.Remove("e:" + entityId);
            _cashAmountByKey[uuid] = cashAmount;
            QueueCashPersist(entityId);
        }
    }

    public void UpdateBridgeVersion(int entityId, int version) {
        if (_bridges.TryGetValue(entityId, out var b)) {
            b.Version = version;
            // Modèle pointeur : l'item peut être simultanément en session poche (slot)
            // et projeté en main — garde les versions OCC des sessions alignées.
            SyncPocketSessionVersions(b.Uuid, version);
        }
    }

    private void SyncPocketSessionVersions(string uuid, int version) {
        if (string.IsNullOrEmpty(uuid)) return;
        foreach (var s in _pocketByPlayer.Values)
            foreach (var it in s.ItemsByEntityId.Values)
                if (it.ItemUuid == uuid) it.Version = version;
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
        public int LeftEntityId;  // -1 = empty (plus jamais assignée — présentation seulement)
        public int BackEntityId;  // -1 = empty (sac à dos porté)

        public static PlayerHandState Empty => new PlayerHandState { RightEntityId = -1, LeftEntityId = -1, BackEntityId = -1 };

        public bool IsHandFree(HandType hand) => GetEntityId(hand) == -1;

        public HandType? GetFreeHand()
        {
            if (RightEntityId == -1) return HandType.Right;
            if (LeftEntityId  == -1) return HandType.Left;
            return null;
        }

        public int GetEntityId(HandType hand) =>
            hand == HandType.Right ? RightEntityId
          : hand == HandType.Back  ? BackEntityId
          : LeftEntityId;

        public void Set(HandType hand, int entityId)
        {
            if      (hand == HandType.Right) RightEntityId = entityId;
            else if (hand == HandType.Back)  BackEntityId  = entityId;
            else                             LeftEntityId  = entityId;
        }

        public void Clear(HandType hand)
        {
            if      (hand == HandType.Right) RightEntityId = -1;
            else if (hand == HandType.Back)  BackEntityId  = -1;
            else                             LeftEntityId  = -1;
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
        _handProjection.Clear();
        _weaponAmmoByKey.Clear();
        _ammoPersistPending.Clear();
        _cashAmountByKey.Clear();
        _cashPersistPending.Clear();
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
            SeedItemStateFromDb(it.configId, it.Id, it.stateData); // arme au sol : réhydrate son chargeur
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

        if (entity.IsHeld) {
            // Capturer le porteur AVANT ClearHolderHandState : celui-ci remet HolderNetId à 0,
            // sinon la projection hotbar (lookup conn + OnProjectedWorldEntityGone) chercherait
            // sur netId=0 et l'entrée poche + le snapshot ne seraient jamais retirés (item
            // consommé qui reste visible dans la hotbar/inventaire).
            uint holderNetId = entity.HolderNetId;
            ClearHolderHandState(entity);
            // Projection hotbar consommée/détruite (ligne DB supprimée ci-dessus) :
            // retire aussi l'entrée poche correspondante + re-push le snapshot.
            NetworkConnectionToClient holderConn = null;
            if (NetworkServer.spawned.TryGetValue(holderNetId, out var holderIdent) && holderIdent != null)
                holderConn = holderIdent.connectionToClient;
            OnProjectedWorldEntityGone(holderNetId, entityId, holderConn);
        }

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

        ItemConfig config = DatabaseManager.ItemConfigs.Find(x => x.ID == entity.ItemConfigId);

        // Distance check — portée définie dans la config de l'item (+ tolérance réseau).
        Vector3 playerPos = conn.identity.transform.position;
        float pickupRange = (config != null ? config.RangeToInteract : 1.5f) + 1.5f;
        if (Vector3.Distance(playerPos, entity.Position) > pickupRange)
        {
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Trop loin" });
            return;
        }

        // Inventaire classique : tout item pocketable ramassé part en poche — la main
        // ne porte que l'item sélectionné via la hotbar ou un objet encombrant (non
        // pocketable). La main n'est donc résolue que pour les encombrants.
        var handState = GetOrCreateHandState(playerNetId);

        HandType? assignedHand = (config != null && config.AllowedInPocket)
            ? (HandType?)null
            : ResolveHand(config, playerNetId);
        if (!assignedHand.HasValue)
        {
            // Main occupée (ou item jamais équipable, ex. munitions) : tout item
            // pocketable part dans un slot de poche libre — comportement « inventaire
            // classique ». Sinon le sac tenu via le fallback colis ci-dessous.
            if (config != null && config.AllowedInPocket
                && TryGetFreePocketSlot(playerNetId, out PocketSession fallbackPocket, out int fallbackSlot)) {
                ApiManager.Instance?.StartCoroutine(
                    PickupToPocketCoroutine(conn, roomId, msg.EntityId, entity, fallbackPocket, fallbackSlot));
                return;
            }

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
            conn.Send(new S2C_PickupResult { Success = false, EntityId = msg.EntityId,
                ErrorMessage = config is AmmoConfig ? InventoryToasts.AmmoNoStorage
                             : (config != null && config.AllowedInPocket ? InventoryToasts.NoSpaceInInventory : "Mains pleines") });
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
        => ItemContainerConfig.Of(cfg)?.IsContainer == true;

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
        // Colis tenu en main prioritaire ; sinon le sac à dos porté (overflow de ramassage).
        int[] held = { hands.RightEntityId, hands.LeftEntityId, hands.BackEntityId };
        foreach (int eid in held) {
            if (eid == -1) continue;
            if (!TryGetEntity(roomId, eid, out var e)) continue;
            var cfg = DatabaseManager.GetItemConfigById(e.ItemConfigId);
            var containerCfg = ItemContainerConfig.Of(cfg);
            if (containerCfg == null || !containerCfg.IsContainer) continue;
            var br = GetBridge(eid);
            if (br == null || string.IsNullOrEmpty(br.Uuid)) continue;
            pkgEntityId = eid; pkgUuid = br.Uuid; pkgCfg = containerCfg; return true;
        }
        return false;
    }

    /// <summary>
    /// Acquisition UNIVERSELLE d'un item dans l'inventaire du joueur : poche en priorité
    /// (item pocketable), main pour les objets encombrants, sinon colis tenu (avec slot
    /// libre), sinon échec. Point unique pour TOUS
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

        if ((config == null || !config.AllowedInPocket) && ResolveHand(config, netId).HasValue) {
            SpawnItemInHand(roomId, configId, conn, config, persistent);
            onSuccess?.Invoke();
            return;
        }
        // Item pocketable : poche d'abord, puis le colis tenu ci-dessous.
        if (config != null && config.AllowedInPocket && TryGetFreePocketSlot(netId, out PocketSession ammoPocket, out int ammoSlot)) {
            string ammoCharId = conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id;
            ApiManager.Instance?.StartCoroutine(
                CreateItemInPocketCoroutine(conn, configId, ammoCharId, ammoPocket, ammoSlot, onSuccess, onFail));
            return;
        }
        if (TryGetHeldPackage(netId, roomId, out _, out string pkgUuid, out ContainerConfig pkgCfg)) {
            string charId = conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id;
            ApiManager.Instance?.StartCoroutine(
                CreateItemInPackageCoroutine(conn, netId, configId, charId, pkgUuid, pkgCfg, onSuccess, onFail));
            return;
        }
        onFail?.Invoke(config != null && config.AllowedInPocket ? InventoryToasts.NoSpaceInInventory : "Mains pleines");
    }

    /// <summary>Pratique : true si le joueur peut recevoir un item (poche libre pour un
    /// pocketable, main libre pour un encombrant, OU colis tenu). La capacité réelle du
    /// colis n'est confirmée qu'à l'écriture (async).</summary>
    public bool CanReceiveItem(NetworkConnectionToClient conn, ItemConfig config) {
        if (conn?.identity == null) return false;
        uint netId = conn.identity.netId;
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null) return false;
        if (config != null && config.AllowedInPocket && TryGetFreePocketSlot(netId, out _, out _)) return true;
        return ((config == null || !config.AllowedInPocket) && ResolveHand(config, netId).HasValue)
            || TryGetHeldPackage(netId, roomId, out _, out _, out _);
    }

    // Cœur partagé : ajoute un item (config) dans le colis tenu. Politique : MERGE D'ABORD
    // dans une pile existante de même config avec place restante (qty < MaxStackSize),
    // sinon POST une nouvelle ligne dans le premier slot libre. Utilisé par le ramassage ET
    // SpawnItemIntoInventory.
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
        PlaceStateJson stateData = null;
        if (stateReq.responseCode == 200) {
            try {
                stateData = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text);
                if (stateData?.items != null) foreach (var it in stateData.items) occupied.Add(ReadSlotIndex(it.stateData));
                // Les meubles emballés occupent aussi des slots → ne pas écraser leur place.
                if (stateData?.props != null) foreach (var p in stateData.props) occupied.Add(ReadSlotIndex(p.stateData));
            } catch { }
        }

        var itemCfg = DatabaseManager.GetItemConfigById(configId);
        if (IsStorageItem(itemCfg)) { onFail?.Invoke(InventoryToasts.NoNestedStorage); yield break; }
        if (itemCfg != null && !pkgCfg.Accepts(itemCfg.Type)) { onFail?.Invoke("Refusé par le colis"); yield break; }

        // ── Tentative MERGE : pile de même config avec place restante. ─────────
        // Évite « Colis plein » quand un slot occupé pourrait absorber une unité supplémentaire.
        int maxStack = itemCfg != null ? itemCfg.MaxStackSize : 1;
        if (maxStack > 1 && stateData?.items != null) {
            foreach (var it in stateData.items) {
                if (it.configId != configId) continue;
                int curQty = Mathf.Max(1, it.quantity);
                if (curQty >= maxStack) continue;
                int slotIdx = ReadSlotIndex(it.stateData);
                var mergeBody = new UpdateItemBody {
                    expectedVersion = it.version,
                    quantity = curQty + 1,
                };
                UnityWebRequest mergeReq = ApiManager.Instance.UpdateItemRequest(it.Id, mergeBody);
                yield return mergeReq.SendWebRequest();
                if (mergeReq.responseCode < 200 || mergeReq.responseCode >= 300) {
                    Debug.LogWarning($"[ServerItemManager] CreateItemInPackage merge PATCH failed code={mergeReq.responseCode} — fallback free slot");
                    break; // tombe sur la branche free-slot ci-dessous
                }
                int newVersion = it.version + 1;
                try { var item = JsonConvert.DeserializeObject<ItemJson>(mergeReq.downloadHandler.text); if (item != null) newVersion = item.version; } catch { }
                // Si le colis est ouvert chez l'opener, met à jour la session in-place.
                if (_openItemContainerByPlayer.TryGetValue(netId, out var openSess) && openSess.PlaceId == placeId) {
                    foreach (var kv in openSess.ItemsByEntityId) {
                        if (kv.Value.ItemUuid == it.Id) {
                            kv.Value.Quantity = curQty + 1;
                            kv.Value.Version = newVersion;
                            if (_bridges.TryGetValue(kv.Key, out var br)) br.Version = newVersion;
                            break;
                        }
                    }
                    SendItemContainerSnapshot(conn, openSess);
                }
                GameLogger.Network.Info("ItemMergeInPackage configId={C} slot={S} newQty={Q} netId={N}",
                    configId, slotIdx, curQty + 1, netId);
                onSuccess?.Invoke();
                yield break;
            }
        }

        int freeSlot = -1;
        for (int i = 0; i < pkgCfg.SlotCount; i++) if (!occupied.Contains(i)) { freeSlot = i; break; }
        if (freeSlot < 0) { onFail?.Invoke("Colis plein"); yield break; }

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
                Quantity = 1,
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

    // ── Équipement hotbar (modèle pointeur) ─────────────────────────────

    /// <summary>
    /// Pointe la main droite sur un slot de poche : déspawne la projection courante et
    /// en spawne une pour l'item du slot (l'item ne bouge pas de sa place — AUCUNE
    /// écriture DB). SlotIndex -1 ou slot vide = mains libres. Main occupée par un VRAI
    /// portage (objet encombrant) → ignoré silencieusement (barre grisée côté client).
    /// </summary>
    public void HandleEquipPocketSlot(NetworkConnectionToClient conn, C2S_EquipPocketSlot msg)
    {
        if (conn?.identity == null) return;
        uint netId    = conn.identity.netId;
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null) return;

        bool hadProjection = _handProjection.TryGetValue(netId, out var current);

        int pocketEntityId = -1;
        PocketSession session = null;
        if (msg.SlotIndex >= 0 && _pocketByPlayer.TryGetValue(netId, out session))
            session.SlotToEntityId.TryGetValue(msg.SlotIndex, out pocketEntityId);
        if (pocketEntityId == 0) pocketEntityId = -1;

        if (hadProjection && current.pocketEntityId == pocketEntityId) return;   // déjà à jour

        ClearHandProjection(netId, roomId);
        if (pocketEntityId < 0 || session == null) return;   // mains libres
        if (!session.ItemsByEntityId.TryGetValue(pocketEntityId, out var pi)) return;

        // Main occupée par un portage réel (encombrant) : on ne projette pas par-dessus.
        var handState = GetOrCreateHandState(netId);
        if (handState.RightEntityId != -1) return;

        int worldEntityId = _nextEntityId++;
        var entity = new ItemEntity {
            EntityId      = worldEntityId,
            RoomId        = roomId,
            ItemConfigId  = pi.ConfigId,
            Position      = conn.identity.transform.position,
            Rotation      = Quaternion.identity,
            HolderNetId   = netId,
            HolderHand    = HandType.Right,
            LocalPosition = Vector3.zero,
            LocalRotation = Quaternion.identity,
            Persistent    = true,
        };
        if (!_rooms.TryGetValue(roomId, out var roomItems)) {
            roomItems = new Dictionary<int, ItemEntity>();
            _rooms[roomId] = roomItems;
        }
        roomItems[worldEntityId] = entity;

        handState.Set(HandType.Right, worldEntityId);
        _playerHands[netId] = handState;

        // Bridge PARTAGÉ avec l'item de poche (même instance) : chargeur d'arme,
        // carburant… PATCHés via l'une ou l'autre entité sans divergence de version.
        ItemDbBridge pocketBridge = GetBridge(pocketEntityId);
        if (pocketBridge != null) _bridges[worldEntityId] = pocketBridge;

        _handProjection[netId] = (worldEntityId, pocketEntityId);

        BroadcastToRoom(roomId, new S2C_SpawnItem {
            EntityId = worldEntityId, RoomId = roomId, ItemConfigId = pi.ConfigId,
            Position = entity.Position, Rotation = entity.Rotation,
            IsHeld = true, HolderNetId = netId, HolderHand = HandType.Right,
            LocalPosition = Vector3.zero, LocalRotation = Quaternion.identity,
        });
        // Liasse projetée en main : pousse son montant (tooltip + bouton Déposer de l'ATM).
        // Le bridge partagé avec l'item de poche résout le montant par uuid.
        if (pi.ConfigId == CashItem.ConfigId)
            BroadcastToRoom(roomId, new S2C_ItemCash { EntityId = worldEntityId, Amount = GetCashAmount(worldEntityId) });
        GameLogger.Network.Info("Item EquipProjection netId={NetId} slot={Slot} config={ConfigId} world={World} pocket={Pocket}",
            netId, msg.SlotIndex, pi.ConfigId, worldEntityId, pocketEntityId);
    }

    /// <summary>Déspawne la projection main du joueur — l'item de poche, lui, ne bouge pas.</summary>
    private void ClearHandProjection(uint netId, string roomId)
    {
        if (!_handProjection.TryGetValue(netId, out var proj)) return;
        _handProjection.Remove(netId);
        _bridges.Remove(proj.worldEntityId);   // bridge partagé : celui de la poche reste intact
        if (_playerHands.TryGetValue(netId, out var hs) && hs.RightEntityId == proj.worldEntityId) {
            hs.Clear(HandType.Right);
            _playerHands[netId] = hs;
        }
        if (roomId != null && _rooms.TryGetValue(roomId, out var roomItems) && roomItems.Remove(proj.worldEntityId)) {
            BroadcastToRoom(roomId, new S2C_DestroyItem { EntityId = proj.worldEntityId, RoomId = roomId });
        }
    }

    /// <summary>L'entrée poche <paramref name="pocketEntityId"/> quitte la session (move,
    /// swap, drop inventaire) : la projection qui la représentait est déspawnée.</summary>
    private void OnPocketEntryRemoved(uint netId, int pocketEntityId)
    {
        if (!_handProjection.TryGetValue(netId, out var proj) || proj.pocketEntityId != pocketEntityId) return;
        string roomId = null;
        foreach (var kv in _rooms) if (kv.Value.ContainsKey(proj.worldEntityId)) { roomId = kv.Key; break; }
        ClearHandProjection(netId, roomId);
    }

    /// <summary>L'entité MAIN <paramref name="worldEntityId"/> d'une projection vient d'être
    /// consommée (DespawnItem — ligne DB supprimée) ou posée au monde (DetachAndPlace —
    /// ligne PATCHée hors poche) : retire aussi l'entrée session poche + re-push le snapshot.</summary>
    private void OnProjectedWorldEntityGone(uint netId, int worldEntityId, NetworkConnectionToClient conn)
    {
        if (!_handProjection.TryGetValue(netId, out var proj) || proj.worldEntityId != worldEntityId) return;
        _handProjection.Remove(netId);
        if (_pocketByPlayer.TryGetValue(netId, out var session)
            && session.ItemsByEntityId.TryGetValue(proj.pocketEntityId, out var pi)) {
            session.ItemsByEntityId.Remove(proj.pocketEntityId);
            if (session.SlotToEntityId.TryGetValue(pi.SlotIndex, out int occ) && occ == proj.pocketEntityId)
                session.SlotToEntityId.Remove(pi.SlotIndex);
            _bridges.Remove(proj.pocketEntityId);
            if (conn != null) SendPocketSnapshot(conn, session);
        }
    }

    public void HandleDrop(NetworkConnectionToClient conn, C2S_RequestDropItem msg)
    {
        if (conn.identity == null) return;

        GameLogger.Network.Debug("Item DropRequest player={PlayerNetId} hand={Hand}", conn.identity.netId, msg.Hand);

        // Drop position: slightly in front of player, snapped to the ground.
        Vector3 dropPos = conn.identity.transform.position + conn.identity.transform.forward * 0.6f;
        if (Physics.Raycast(dropPos + Vector3.up, Vector3.down, out var hit, 5f, 1 << 9))
            dropPos = hit.point;

        if (DetachAndPlace(conn, msg.Hand, dropPos, Quaternion.identity, out string error))
            conn.Send(new S2C_DropResult { Success = true, Hand = msg.Hand });
        else
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = error });
    }

    /// <summary>
    /// « Poser » l'item tenu à un emplacement explicite (le personnage a déjà marché jusqu'au
    /// point de pose côté client). Même détachement/persistance que le drop, mais la position
    /// vient du message (choisie par le joueur), pas du « devant le joueur ».
    /// </summary>
    public void HandlePoseHeldItem(NetworkConnectionToClient conn, C2S_PoseHeldItem msg)
    {
        if (conn.identity == null) return;

        GameLogger.Network.Debug("Item PoseRequest player={PlayerNetId} hand={Hand}", conn.identity.netId, msg.Hand);

        if (DetachAndPlace(conn, msg.Hand, msg.Position, msg.Rotation, out string error))
            conn.Send(new S2C_DropResult { Success = true, Hand = msg.Hand });
        else
            conn.Send(new S2C_DropResult { Success = false, Hand = msg.Hand, ErrorMessage = error });
    }

    /// <summary>
    /// Repositionne un item-monde DÉJÀ posé (pas en main) vers une nouvelle position/rotation.
    /// Émis quand on déplace en build mode un prop dont la surface posable portait des items :
    /// l'item suit la surface pour ne pas « flotter ». Même persistance/broadcast que
    /// <see cref="DetachAndPlace"/>, mais aucune main n'est impliquée (on ne touche pas
    /// <see cref="_playerHands"/>) : on met simplement à jour l'entité existante.
    /// </summary>
    public void HandleMoveWorldItem(NetworkConnectionToClient conn, C2S_MoveWorldItem msg)
    {
        if (conn.identity == null) return;

        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null) return;
        if (!TryGetEntity(roomId, msg.EntityId, out var entity)) return;
        // Un item tenu n'est pas « posé » : ne jamais le téléporter via ce chemin.
        if (entity.IsHeld) return;

        entity.Position = msg.Position;
        entity.Rotation = msg.Rotation;

        GameLogger.Network.Info("Item MoveWorld player={PlayerNetId} entity={EntityId} room={RoomId}",
            conn.identity.netId, msg.EntityId, roomId);

        // Persistance pilotée par ItemConfig.ToPersist (UUID conservé) : seul un item-monde
        // persistant a une position stockée à mettre à jour. Un item non-persistant n'a déjà
        // plus de ligne DB (supprimée à son drop initial) — rien à faire ici.
        ItemConfig cfg = DatabaseManager.GetItemConfigById(entity.ItemConfigId);
        if (cfg != null && cfg.ToPersist) {
            string worldPlaceId = ResolveWorldPlaceId(conn, roomId);
            if (!string.IsNullOrEmpty(worldPlaceId) && GetBridge(msg.EntityId) != null)
                PersistMoveToWorldAsync(msg.EntityId, worldPlaceId, msg.Position, msg.Rotation);
        }

        BroadcastToRoom(roomId, new S2C_ItemDetachedFromHand
        {
            EntityId      = msg.EntityId,
            WorldPosition = msg.Position,
            WorldRotation = msg.Rotation
        });
    }

    /// <summary>
    /// Détache l'item tenu dans <paramref name="hand"/> et le pose à la position/rotation monde
    /// données : maj de l'entité, libération de la main, persistance pilotée par ItemConfig.ToPersist
    /// (UNIFORME, aucun cas spécial colis), puis broadcast <see cref="S2C_ItemDetachedFromHand"/> à
    /// la room. Partagé par le drop (devant le joueur) et la pose (emplacement choisi). Renvoie faux
    /// + <paramref name="error"/> en cas d'échec de validation (pas dans une pièce, main vide…).
    /// </summary>
    private bool DetachAndPlace(NetworkConnectionToClient conn, HandType hand,
        Vector3 worldPos, Quaternion worldRot, out string error)
    {
        error = null;
        uint playerNetId = conn.identity.netId;
        string roomId    = PlayerRoomTracker.Instance.GetRoom(conn);

        if (roomId == null)         { error = "Pas dans une pièce"; return false; }
        if (!_playerHands.TryGetValue(playerNetId, out var handState)) { error = "Aucun objet en main"; return false; }

        int entityId = handState.GetEntityId(hand);
        if (entityId == -1)         { error = "Main vide"; return false; }
        if (!TryGetEntity(roomId, entityId, out var entity)) { error = "Objet introuvable"; return false; }

        entity.Position = worldPos;
        entity.Rotation = worldRot;
        ClearHolderHandState(entity);

        // Projection hotbar posée au monde : l'item quitte la poche — retire l'entrée
        // session + re-push le snapshot (la ligne DB est PATCHée/supprimée ci-dessous).
        OnProjectedWorldEntityGone(playerNetId, entityId, conn);

        GameLogger.Network.Info("Item DetachPlace player={PlayerNetId} entity={EntityId} hand={Hand} room={RoomId}",
            playerNetId, entityId, hand, roomId);

        // Persistance au lâcher, pilotée par ItemConfig.ToPersist (UNIFORME — aucun cas
        // spécial colis) :
        //  - ToPersist → l'item devient un item-monde PERSISTANT : on PATCH sa place vers la
        //    place de la pièce + sa position (UUID conservé → survit au redémarrage et se
        //    recharge au sol via ApartmentController). C'est ce qui préserve aussi la place
        //    "item_container:{uuid}" d'un colis et son contenu.
        //  - sinon → on supprime sa ligne DB (item-monde éphémère, comportement historique).
        ItemConfig dropCfg = DatabaseManager.GetItemConfigById(entity.ItemConfigId);
        if (dropCfg != null && dropCfg.ToPersist) {
            string worldPlaceId = ResolveWorldPlaceId(conn, roomId);
            if (!string.IsNullOrEmpty(worldPlaceId) && GetBridge(entityId) != null)
                PersistMoveToWorldAsync(entityId, worldPlaceId, worldPos, worldRot);
        }
        else if (entity.Persistent) {
            PersistDropAsync(entityId);
        }

        BroadcastToRoom(roomId, new S2C_ItemDetachedFromHand
        {
            EntityId      = entityId,
            WorldPosition = worldPos,
            WorldRotation = worldRot
        });
        return true;
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
        if (ItemContainerConfig.Of(cfg)?.IsContainer == true) {
            int handEntity = SpawnItemInHand(roomId, msg.ItemConfigId, conn, cfg, persistent: true);
            GameLogger.Network.Info("Package AdminSpawn entity={EntityId} configId={ItemConfigId} room={RoomId} conn={ConnId}",
                handEntity, msg.ItemConfigId, roomId, conn.connectionId);
            return;
        }

        int entityId = SpawnItem(roomId, msg.ItemConfigId, msg.Position, Quaternion.identity);
        GameLogger.Network.Info("Item AdminSpawn entity={EntityId} configId={ItemConfigId} room={RoomId} conn={ConnId}",
            entityId, msg.ItemConfigId, roomId, conn.connectionId);
    }

    // ── Fouille de cadavres (loot) ────────────────────────────────────────────

    // Portée de fouille : règle 2 m des personnages + marge (le ragdoll s'étale).
    private const float CorpseSearchRange = 3f;

    /// <summary>Taille de la grille de butin d'un cadavre de NPC (slots). Partagé avec
    /// l'ouverture optimiste client (ClientNpcView.OnSearchExecuted).</summary>
    public const int CorpseLootSlots = 6;

    // Place backend de chaque butin de cadavre, par clé de loot (NpcAIController) :
    // renseignée à la première fouille, consommée par CleanupCorpseLoot au despawn.
    private readonly Dictionary<string, string> _corpseLootPlaceIds = new Dictionary<string, string>();

    /// <summary>Fouille le cadavre d'un NPC mort : ouvre sa grille de butin dans le panneau
    /// conteneur standard (canal Prop, même flux que le coffre de véhicule — place
    /// "container:{lootKey}"). Le loot (cash + entrées de la NpcConfig) est tiré et créé en
    /// DB à la PREMIÈRE fouille ; les suivantes rouvrent le restant. Le client a déjà ouvert
    /// la grille optimistement : tout refus répond S2C_ContainerOpenFailed (PropId 0).</summary>
    public void HandleSearchNpcCorpse(NetworkConnectionToClient conn, C2S_SearchNpcCorpse msg)
    {
        if (conn?.identity == null) return;

        if (!NpcAIController.TryGet(msg.NpcId, out NpcAIController npc) || npc == null
            || !npc.TryBeginSearch(out string lootKey, out bool firstSearch)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Il n'y a rien à fouiller ici" });
            return;
        }
        if (!WithinSearchRange(conn.identity.transform.position, npc.transform.position)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Trop loin" });
            return;
        }
        string charId = conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Sans caractère" });
            return;
        }

        ClosePropSession(conn.identity.netId); // un seul conteneur « prop » ouvert à la fois
        ApiManager.Instance?.StartCoroutine(
            OpenCorpseLootCoroutine(conn, conn.identity.netId, npc, lootKey, firstSearch, charId));
    }

    /// <summary>Première fouille : POST de la place "container:{lootKey}" puis création des
    /// lignes de butin (liasse de cash avec stateData.cashAmount + items de la table de loot,
    /// slots séquentiels). Dans tous les cas, enchaîne sur le flux d'ouverture standard
    /// (OpenContainerCoroutine, idempotent sur la place) qui pousse S2C_ContainerOpened.</summary>
    private IEnumerator OpenCorpseLootCoroutine(NetworkConnectionToClient conn, uint netId,
        NpcAIController npc, string lootKey, bool firstSearch, string charId)
    {
        if (firstSearch) {
            // 1. Place du butin.
            var createPlace = new CreatePlaceBody {
                placeKey = $"container:{lootKey}",
                type = "container",
                ownerId = charId,
                properties = new Dictionary<string, object> { { "slotCount", CorpseLootSlots } },
            };
            UnityWebRequest placeReq = ApiManager.Instance.CreatePlaceRequest(createPlace);
            yield return placeReq.SendWebRequest();
            if (placeReq.responseCode < 200 || placeReq.responseCode >= 300) {
                conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Erreur place" });
                yield break;
            }
            string placeId = null;
            try { var p = JsonConvert.DeserializeObject<PlaceJson>(placeReq.downloadHandler.text); placeId = p?.Id; } catch { }
            if (string.IsNullOrEmpty(placeId)) {
                conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Place sans id" });
                yield break;
            }
            _corpseLootPlaceIds[lootKey] = placeId;

            // 2. Tirage du loot (une seule fois par corps) : liasse + entrées de la config.
            Sim.NPC.NpcConfig cfg = npc != null ? npc.Config : null;
            int slot = 0;
            int cash = cfg != null && cfg.LootCashMax > 0 ? Random.Range(cfg.LootCashMin, cfg.LootCashMax + 1) : 0;
            if (cash > 0 && slot < CorpseLootSlots) {
                var cashBody = new CreateItemBody {
                    placeId = placeId, configId = CashItem.ConfigId, quantity = 1,
                    stateData = new Dictionary<string, object> {
                        { "slotIndex", slot++ },
                        { StateKeyCashAmount, cash },
                    },
                };
                yield return ApiManager.Instance.CreateItemRequest(cashBody).SendWebRequest();
            }
            if (cfg != null) {
                foreach (Sim.NPC.NpcLootEntry entry in cfg.Loot) {
                    if (slot >= CorpseLootSlots) break;
                    if (entry == null || entry.item == null || Random.value > entry.chance) continue;
                    var itemBody = new CreateItemBody {
                        placeId = placeId, configId = entry.item.ID,
                        quantity = Mathf.Max(1, entry.quantity),
                        stateData = new Dictionary<string, object> { { "slotIndex", slot++ } },
                    };
                    yield return ApiManager.Instance.CreateItemRequest(itemBody).SendWebRequest();
                }
            }
            GameLogger.Player.Info("NpcCorpseLootGenerated {LooterNetId} {NpcId} {Cash} {Slots}",
                netId, npc != null ? npc.NpcId : -1, cash, slot);
        }

        // 3. Ouverture standard (place idempotente, state fetch, session, S2C_ContainerOpened).
        yield return OpenContainerCoroutine(conn, netId, 0, lootKey, charId,
            ContainerConfig.CreateRuntime(CorpseLootSlots), broadcastVisual: false);
    }

    /// <summary>Purge le butin restant d'un cadavre au despawn du NPC : supprime chaque
    /// ligne encore dans la place de loot (les items déjà ramassés ont changé de place et ne
    /// sont PAS touchés). Appelé par NpcAIController.OnDisable.</summary>
    public void CleanupCorpseLoot(string lootKey)
    {
        if (string.IsNullOrEmpty(lootKey) || !_corpseLootPlaceIds.TryGetValue(lootKey, out string placeId)) return;
        _corpseLootPlaceIds.Remove(lootKey);
        ApiManager.Instance?.StartCoroutine(CleanupCorpseLootCoroutine(placeId));
    }

    private IEnumerator CleanupCorpseLootCoroutine(string placeId)
    {
        UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
        yield return stateReq.SendWebRequest();
        if (stateReq.responseCode != 200) yield break;
        PlaceStateJson state = null;
        try { state = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text); } catch { }
        if (state?.items == null) yield break;
        foreach (var it in state.items) {
            if (string.IsNullOrEmpty(it.Id)) continue;
            yield return ApiManager.Instance.DeleteItemRequest(it.Id).SendWebRequest();
        }
        GameLogger.Network.Info("NpcCorpseLootCleaned placeId={PlaceId} items={Count}", placeId, state.items.Length);
    }

    /// <summary>Taille de la grille de butin d'un cadavre de JOUEUR (poche 8 + main + dos).
    /// Partagé avec l'ouverture optimiste client (LootBag).</summary>
    public const int PlayerCorpseLootSlots = 10;

    // Durée de vie max d'un sac de butin (s) avant disparition automatique.
    private const float LootBagLifetimeSeconds = 600f; // 10 min

    // Sacs de butin actifs, par clé de loot (= place "container:{clé}"). Le GameObject est un
    // objet réseau Mirror (prefab LootBag) spawné à la mort d'un joueur.
    private readonly Dictionary<string, GameObject> _lootBags = new Dictionary<string, GameObject>();
    private GameObject _lootBagPrefab;

    /// <summary>Laisse tomber un SAC DE BUTIN à l'endroit où le joueur vient de mourir :
    /// déplace TOUT ce qu'il possède (mains, dos + sac et contenu, poche) dans une place de
    /// butin "container:{clé}" (identité/UUID conservés), puis spawne un LootBag réseau qui
    /// vit indépendamment de la victime (elle respawn) — fouillable par quiconque pendant
    /// <see cref="LootBagLifetimeSeconds"/> ou jusqu'à ce qu'il soit vidé. Appelé par
    /// PlayerController.Kill côté serveur.</summary>
    public void DropPlayerLootBag(Sim.PlayerController victim)
    {
        if (victim == null) return;
        NetworkConnectionToClient victimConn = victim.connectionToClient;
        string victimCharId = victim.CharacterData?.Id;

        // Détache SYNCHRONEMENT les possessions de la victime et récupère les lignes à ranger
        // dans le sac. Rien à lâcher → pas de sac (« tant qu'il a du loot »).
        var belongings = CollectAndClearVictimBelongings(victim, victimConn);
        if (belongings.Count == 0) return;

        ApiManager.Instance?.StartCoroutine(DropLootBagCoroutine(
            belongings, victim.transform.position, victim.transform.rotation,
            victimCharId, "Sac de butin"));
    }

    private IEnumerator DropLootBagCoroutine(List<(string uuid, int version, int configId)> toPlace,
        Vector3 pos, Quaternion rot, string ownerCharId, string label)
    {
        string lootKey = System.Guid.NewGuid().ToString("N");

        // 1. Place de butin.
        var createPlace = new CreatePlaceBody {
            placeKey = $"container:{lootKey}",
            type = "container",
            ownerId = ownerCharId,
            properties = new Dictionary<string, object> { { "slotCount", PlayerCorpseLootSlots } },
        };
        UnityWebRequest placeReq = ApiManager.Instance.CreatePlaceRequest(createPlace);
        yield return placeReq.SendWebRequest();
        if (placeReq.responseCode < 200 || placeReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] LootBag place create failed code={placeReq.responseCode}");
            yield break;
        }
        string placeId = null;
        try { var p = JsonConvert.DeserializeObject<PlaceJson>(placeReq.downloadHandler.text); placeId = p?.Id; } catch { }
        if (string.IsNullOrEmpty(placeId)) yield break;
        _corpseLootPlaceIds[lootKey] = placeId;

        // 2. Déplace chaque possession dans la place (PATCH, identité conservée) ou crée une
        //    ligne neuve pour un item de main éphémère (sans UUID).
        int slot = 0;
        foreach ((string uuid, int version, int configId) in toPlace) {
            if (slot >= PlayerCorpseLootSlots) break;
            if (!string.IsNullOrEmpty(uuid)) {
                var body = new UpdateItemBody {
                    expectedVersion = Mathf.Max(1, version),
                    placeId = placeId,
                    stateData = new Dictionary<string, object> { { "slotIndex", slot } },
                };
                yield return ApiManager.Instance.UpdateItemRequest(uuid, body).SendWebRequest();
            } else {
                var body = new CreateItemBody {
                    placeId = placeId, configId = configId, quantity = 1,
                    stateData = new Dictionary<string, object> { { "slotIndex", slot } },
                };
                yield return ApiManager.Instance.CreateItemRequest(body).SendWebRequest();
            }
            slot++;
        }

        // 3. Spawne le sac réseau au point de mort.
        if (_lootBagPrefab == null) _lootBagPrefab = Resources.Load<GameObject>("Prefabs/LootBag");
        if (_lootBagPrefab == null) {
            Debug.LogWarning("[ServerItemManager] LootBag prefab introuvable (Resources/Prefabs/LootBag)");
            yield break;
        }
        GameObject bag = UnityEngine.Object.Instantiate(_lootBagPrefab, pos, rot);
        LootBag bagComp = bag.GetComponent<LootBag>();
        if (bagComp != null) { bagComp.lootKey = lootKey; bagComp.label = label; }
        NetworkServer.Spawn(bag);
        _lootBags[lootKey] = bag;

        GameLogger.Player.Info("LootBagDropped lootKey={LootKey} items={Count} placeId={PlaceId}",
            lootKey, slot, placeId);

        // 4. Disparition automatique après la durée de vie max (le vidage anticipé est géré
        //    par ClosePropSession → TryDespawnEmptyLootBag).
        yield return new WaitForSeconds(LootBagLifetimeSeconds);
        DespawnLootBag(lootKey);
    }

    /// <summary>Ouvre la grille de butin d'un SAC (LootBag) : résout le sac par netId, vérifie
    /// la portée, puis ouvre sa place "container:{lootKey}" dans le panneau conteneur standard
    /// (le client a déjà ouvert optimistement). Fouillable par n'importe quel joueur.</summary>
    public void HandleSearchLootBag(NetworkConnectionToClient conn, C2S_SearchLootBag msg)
    {
        if (conn?.identity == null) return;

        if (!NetworkServer.spawned.TryGetValue(msg.BagNetId, out NetworkIdentity bagIdentity)
            || bagIdentity == null || !bagIdentity.TryGetComponent(out LootBag bag)
            || string.IsNullOrEmpty(bag.lootKey)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Sac introuvable" });
            return;
        }
        if (!WithinSearchRange(conn.identity.transform.position, bag.transform.position)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Trop loin" });
            return;
        }
        string looterCharId = conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id;
        if (string.IsNullOrEmpty(looterCharId)) {
            conn.Send(new S2C_ContainerOpenFailed { PropId = 0, ErrorMessage = "Sans caractère" });
            return;
        }

        ClosePropSession(conn.identity.netId);
        ApiManager.Instance?.StartCoroutine(OpenContainerCoroutine(conn, conn.identity.netId, 0,
            bag.lootKey, looterCharId, ContainerConfig.CreateRuntime(PlayerCorpseLootSlots),
            broadcastVisual: false));
    }

    /// <summary>Détruit un sac de butin (expiration 10 min OU vidé) et purge le restant de sa
    /// place de loot.</summary>
    private void DespawnLootBag(string lootKey)
    {
        if (string.IsNullOrEmpty(lootKey)) return;
        if (_lootBags.TryGetValue(lootKey, out GameObject bag)) {
            _lootBags.Remove(lootKey);
            if (bag != null) NetworkServer.Destroy(bag);
        }
        CleanupCorpseLoot(lootKey); // DELETE les lignes restantes + oublie la place
        GameLogger.Player.Info("LootBagDespawned lootKey={LootKey}", lootKey);
    }

    /// <summary>Si <paramref name="placeId"/> est la place d'un sac de butin désormais VIDE,
    /// fait disparaître le sac immédiatement (appelé à la fermeture d'une session conteneur).</summary>
    private void TryDespawnEmptyLootBag(string placeId)
    {
        if (string.IsNullOrEmpty(placeId)) return;
        string lootKey = null;
        foreach (var kv in _corpseLootPlaceIds) if (kv.Value == placeId) { lootKey = kv.Key; break; }
        if (lootKey != null && _lootBags.ContainsKey(lootKey)) DespawnLootBag(lootKey);
    }

    /// <summary>Détache (runtime) tout ce que porte la victime et renvoie la liste des lignes
    /// à ranger dans la grille de butin : UUID (PATCH de place) quand l'item est persisté,
    /// sinon configId (POST d'une nouvelle ligne pour un item de main éphémère). Vide les
    /// mains, la poche et la projection hotbar, et re-pousse un snapshot poche vide.</summary>
    private List<(string uuid, int version, int configId)> CollectAndClearVictimBelongings(
        Sim.PlayerController victim, NetworkConnectionToClient victimConn)
    {
        var result = new List<(string, int, int)>();
        string roomId = PlayerRoomTracker.Instance.GetRoom(victimConn);

        // Projection hotbar : l'entité de main partage la ligne DB de l'entrée poche. On
        // retire l'entité de main (visuel) ; la ligne DB sera traitée via la poche ci-dessous.
        if (_handProjection.TryGetValue(victim.netId, out var proj)) {
            if (roomId != null && TryGetEntity(roomId, proj.worldEntityId, out var pe)) {
                ClearHolderHandState(pe);
                _rooms[roomId].Remove(proj.worldEntityId);
                BroadcastToRoom(roomId, new S2C_DestroyItem { EntityId = proj.worldEntityId, RoomId = roomId });
            }
            _handProjection.Remove(victim.netId);
        }

        // Items RÉELLEMENT en main / sur le dos (hors projection déjà retirée).
        var handState = GetOrCreateHandState(victim.netId);
        foreach (HandType hand in new[] { HandType.Right, HandType.Left, HandType.Back }) {
            int entityId = handState.GetEntityId(hand);
            if (entityId == -1 || roomId == null || !TryGetEntity(roomId, entityId, out var entity)) continue;
            ItemDbBridge bridge = GetBridge(entityId);
            result.Add((bridge?.Uuid, bridge?.Version ?? 0, entity.ItemConfigId));
            ClearHolderHandState(entity);
            _rooms[roomId].Remove(entityId);
            _bridges.Remove(entityId);
            BroadcastToRoom(roomId, new S2C_DestroyItem { EntityId = entityId, RoomId = roomId });
        }

        // Poche : chaque entrée (UUID persistant) part dans la grille.
        if (_pocketByPlayer.TryGetValue(victim.netId, out PocketSession pocket)) {
            foreach (PocketSessionItem pi in pocket.ItemsByEntityId.Values)
                result.Add((pi.ItemUuid, pi.Version, pi.ConfigId));
            foreach (int entityId in new List<int>(pocket.ItemsByEntityId.Keys)) _bridges.Remove(entityId);
            pocket.ItemsByEntityId.Clear();
            pocket.SlotToEntityId.Clear();
            SendPocketSnapshot(victimConn, pocket); // poche vidée côté victime
        }

        return result;
    }

    private static bool WithinSearchRange(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        d.y = 0f;
        return d.sqrMagnitude <= CorpseSearchRange * CorpseSearchRange;
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

        // Force the leaver to forget every other item in the room (items held by
        // remote players, ground items). Without this the client keeps stale
        // _items entries for those entityIds; on re-entry SendRoomSnapshot resends
        // the same entityIds and OnSpawnItem's dedup guard silently drops them,
        // leaving the leaver unable to see those held items until their entityId
        // changes (i.e. until the holder themselves leaves a room and re-spawns
        // them under a fresh id).
        if (_rooms.TryGetValue(roomId, out var roomItems)) {
            foreach (var entity in roomItems.Values) {
                conn.Send(new S2C_DestroyItem { EntityId = entity.EntityId, RoomId = roomId });
            }
        }
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
            // Liasse : pousse son montant au client qui rejoint (tooltip + ATM).
            if (entity.ItemConfigId == CashItem.ConfigId)
                conn.Send(new S2C_ItemCash { EntityId = entity.EntityId, Amount = GetCashAmount(entity.EntityId) });
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

        // Règle d'armement : UNE seule arme à la fois (à feu OU de mêlée) — une arme déjà
        // en main bloque l'équipement de toute autre arme, quelle que soit la main libre.
        if (IsWeaponConfig(config) && HoldsWeapon(netId)) return null;

        if (config.HandleType == ItemHandleType.TWO_HAND)
            return (state.RightEntityId == -1 && state.LeftEntityId == -1) ? HandType.Right : (HandType?)null;

        return FreeHandRespectingTwoHand(state);
    }

    /// <summary>Le joueur tient-il déjà une arme (à feu ou de mêlée) dans l'une de ses mains ?</summary>
    private bool HoldsWeapon(uint netId)
    {
        var state = GetOrCreateHandState(netId);
        return EntityIsWeapon(state.RightEntityId) || EntityIsWeapon(state.LeftEntityId);
    }

    private static bool IsWeaponConfig(ItemConfig config)
        => config is WeaponConfig || config is MeleeWeaponConfig;

    private bool EntityIsWeapon(int entityId)
    {
        if (entityId == -1) return false;
        foreach (var room in _rooms.Values)
            if (room.TryGetValue(entityId, out var e))
                return IsWeaponConfig(DatabaseManager.GetItemConfigById(e.ItemConfigId));
        return false;
    }

    /// <summary>Modèle « un item actif » (phase 3) : seule la main droite est un slot
    /// logique — la gauche n'est plus jamais assignée (les items hand_left résiduels
    /// sont migrés en poche au connect via MigrateLeftHandToPocket).</summary>
    private HandType? FreeHandRespectingTwoHand(PlayerHandState state)
    {
        return state.RightEntityId == -1 ? HandType.Right : (HandType?)null;
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
    /// Config de l'arme (WeaponConfig) actuellement tenue en main par le joueur dans sa
    /// pièce, ou null. Consommé par PlayerShooter.CmdFire pour valider un tir côté serveur
    /// — un client ne peut pas tirer sans arme réellement en main.
    /// </summary>
    public WeaponConfig GetHeldWeaponConfig(uint playerNetId, string roomId)
        => TryGetHeldWeapon(playerNetId, roomId, out _, out WeaponConfig weapon) ? weapon : null;

    /// <summary>Comme <see cref="GetHeldWeaponConfig"/> mais renvoie aussi l'entityId de
    /// l'arme — nécessaire pour adresser son chargeur (Get/SetWeaponLoadedAmmo).</summary>
    public bool TryGetHeldWeapon(uint playerNetId, string roomId, out int weaponEntityId, out WeaponConfig config)
    {
        weaponEntityId = -1;
        config = null;
        if (roomId == null || !_rooms.TryGetValue(roomId, out var roomItems)) return false;
        foreach (var entity in roomItems.Values) {
            if (entity.HolderNetId != playerNetId) continue;
            if (DatabaseManager.GetItemConfigById(entity.ItemConfigId) is WeaponConfig weapon) {
                weaponEntityId = entity.EntityId;
                config = weapon;
                return true;
            }
        }
        return false;
    }

    /// <summary>Les DEUX mains du joueur sont-elles vides ? Le sac porté sur le dos ne
    /// compte pas. Consommé par PlayerMelee.CmdPunch : le coup de poing n'est permis que
    /// mains nues.</summary>
    public bool PlayerHandsAreEmpty(uint playerNetId)
    {
        var state = GetOrCreateHandState(playerNetId);
        return state.RightEntityId == -1 && state.LeftEntityId == -1;
    }

    /// <summary>Comme <see cref="TryGetHeldWeapon"/> mais pour une arme de MÊLÉE
    /// (MeleeWeaponConfig). Consommé par PlayerMelee.CmdAttack pour valider un coup côté
    /// serveur — un client ne peut pas frapper sans arme réellement en main.</summary>
    public bool TryGetHeldMeleeWeapon(uint playerNetId, string roomId, out int weaponEntityId, out MeleeWeaponConfig config)
    {
        weaponEntityId = -1;
        config = null;
        if (roomId == null || !_rooms.TryGetValue(roomId, out var roomItems)) return false;
        foreach (var entity in roomItems.Values) {
            if (entity.HolderNetId != playerNetId) continue;
            if (DatabaseManager.GetItemConfigById(entity.ItemConfigId) is MeleeWeaponConfig melee) {
                weaponEntityId = entity.EntityId;
                config = melee;
                return true;
            }
        }
        return false;
    }

    // ── Chargeurs d'armes (munitions) ─────────────────────────────────────────
    // État VOLATILE du chargeur de chaque arme. Clé = uuid DB de l'item (stable sur
    // drop / room change / reconnect pendant la session serveur) ou "e:{entityId}" le
    // temps que le bridge arrive (persist pickup asynchrone) — AssociateUuid migre la
    // clé. Une arme jamais vue a son chargeur PLEIN (comme le bidon d'essence spawn
    // plein) ; au restart serveur tout redevient plein (pas de persistance DB en V1).

    private readonly Dictionary<string, int> _weaponAmmoByKey = new Dictionary<string, int>();

    private string WeaponAmmoKey(int entityId) {
        var bridge = GetBridge(entityId);
        return bridge != null && !string.IsNullOrEmpty(bridge.Uuid) ? bridge.Uuid : "e:" + entityId;
    }

    /// <summary>Balles restantes dans le chargeur de l'arme <paramref name="weaponEntityId"/>.
    /// Première consultation = chargeur plein (mémorisé).</summary>
    public int GetWeaponLoadedAmmo(int weaponEntityId, WeaponConfig config) {
        string key = WeaponAmmoKey(weaponEntityId);
        if (_weaponAmmoByKey.TryGetValue(key, out int loaded)) return loaded;
        int full = config != null ? config.MagazineSize : 0;
        _weaponAmmoByKey[key] = full;
        return full;
    }

    public void SetWeaponLoadedAmmo(int weaponEntityId, int value) {
        _weaponAmmoByKey[WeaponAmmoKey(weaponEntityId)] = Mathf.Max(0, value);
        QueueWeaponAmmoPersist(weaponEntityId);
    }

    // ── Persistance du chargeur (stateData.loadedAmmo sur la ligne de l'arme) ──
    // Le PATCH backend REMPLACE stateData intégralement : l'écriture réinjecte donc le
    // slotIndex courant si l'arme est posée dans une poche/un conteneur ouvert. Débouncé
    // (une écriture par rafale) ; déclenché par chaque Set (tir/reload). À la relecture
    // (restore mains/poche/conteneurs/items-monde), SeedWeaponAmmoFromDb réhydrate la
    // mémoire — qui garde toujours la priorité sur la DB (plus fraîche que le débounce).

    private const string StateKeyLoadedAmmo = "loadedAmmo";
    private const float AmmoPersistDebounce = 1.2f;

    private readonly HashSet<string> _ammoPersistPending = new HashSet<string>();

    private void QueueWeaponAmmoPersist(int weaponEntityId) {
        ItemDbBridge bridge = GetBridge(weaponEntityId);
        // Pas encore de ligne DB (persist pickup asynchrone) : AssociateUuid migrera la clé
        // mémoire, le prochain changement de chargeur persistera.
        if (bridge == null || string.IsNullOrEmpty(bridge.Uuid) || ApiManager.Instance == null) return;
        if (!_ammoPersistPending.Add(bridge.Uuid)) return; // écriture déjà programmée
        ApiManager.Instance.StartCoroutine(PersistWeaponAmmoCoroutine(bridge.Uuid));
    }

    private IEnumerator PersistWeaponAmmoCoroutine(string uuid) {
        yield return new WaitForSeconds(AmmoPersistDebounce);
        _ammoPersistPending.Remove(uuid);
        if (!_weaponAmmoByKey.TryGetValue(uuid, out int loaded)) yield break;

        // Bridge re-résolu PAR UUID au moment de l'écriture : l'arme a pu changer de place
        // (et d'entityId) pendant le débounce. Plus de bridge = plus adressable (ex. room
        // vidée) — la valeur reste en mémoire, un prochain Set re-tentera.
        if (!TryGetBridgeByUuid(uuid, out int entityId, out ItemDbBridge bridge)) yield break;

        var state = new Dictionary<string, object> { { StateKeyLoadedAmmo, loaded } };
        if (TryGetSlotIndexByUuid(uuid, out int slotIndex)) state["slotIndex"] = slotIndex;

        var body = new UpdateItemBody { expectedVersion = bridge.Version, stateData = state };
        UnityWebRequest req = ApiManager.Instance.UpdateItemRequest(uuid, body);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300) {
            // 409/404 possibles si la ligne a bougé entre-temps : on abandonne sans bruit
            // majeur — la mémoire serveur reste juste, la DB se rattrapera au prochain Set.
            Debug.LogWarning($"[ServerItemManager] PersistWeaponAmmo failed code={req.responseCode} uuid={uuid}");
            yield break;
        }
        try {
            var it = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text);
            if (it != null) UpdateBridgeVersion(entityId, it.version);
        } catch { }
    }

    private bool TryGetBridgeByUuid(string uuid, out int entityId, out ItemDbBridge bridge) {
        foreach (var kv in _bridges) {
            if (kv.Value.Uuid == uuid) { entityId = kv.Key; bridge = kv.Value; return true; }
        }
        entityId = -1;
        bridge = null;
        return false;
    }

    /// <summary>Slot occupé par l'item <paramref name="uuid"/> dans une session poche ou
    /// conteneur ouvert — pour le réécrire dans un PATCH stateData (remplacement intégral).
    /// Faux = l'item est en main ou au sol (pas de slotIndex).</summary>
    private bool TryGetSlotIndexByUuid(string uuid, out int slotIndex) {
        foreach (var s in _pocketByPlayer.Values)
            foreach (var it in s.ItemsByEntityId.Values)
                if (it.ItemUuid == uuid) { slotIndex = it.SlotIndex; return true; }
        foreach (var s in _openContainerByPlayer.Values)
            foreach (var it in s.ItemsByEntityId.Values)
                if (!it.IsProp && it.ItemUuid == uuid) { slotIndex = it.SlotIndex; return true; }
        foreach (var s in _openItemContainerByPlayer.Values)
            foreach (var it in s.ItemsByEntityId.Values)
                if (!it.IsProp && it.ItemUuid == uuid) { slotIndex = it.SlotIndex; return true; }
        slotIndex = 0;
        return false;
    }

    /// <summary>Premier slot de poche libre du joueur. Faux si la session poche n'est pas
    /// (encore) ouverte ou si la poche est pleine.</summary>
    private bool TryGetFreePocketSlot(uint netId, out PocketSession session, out int slotIndex) {
        slotIndex = -1;
        if (!_pocketByPlayer.TryGetValue(netId, out session)) return false;
        for (int i = 0; i < PocketCapacity; i++) {
            if (!session.SlotToEntityId.ContainsKey(i)) { slotIndex = i; return true; }
        }
        return false;
    }

    /// <summary>Ramassage d'un item-monde DIRECTEMENT vers une poche — pour les items jamais
    /// équipables en main (munitions). Item bridgé → PATCH de sa ligne vers la place poche
    /// (uuid conservé) ; éphémère → nouvelle ligne. L'entité monde est retirée de la room en
    /// PRÉSERVANT la ligne DB (contrairement à DespawnItem qui la supprime). Un seul flux
    /// poche par joueur en pratique (ses propres actions) — pas de re-check du slot après
    /// l'aller-retour backend.</summary>
    private IEnumerator PickupToPocketCoroutine(NetworkConnectionToClient conn, string roomId,
        int entityId, ItemEntity entity, PocketSession pocket, int slotIndex) {
        string itemUuid = null;
        int version = 0;
        ItemDbBridge bridge = GetBridge(entityId);

        if (bridge != null && !string.IsNullOrEmpty(bridge.Uuid)) {
            var body = new UpdateItemBody {
                expectedVersion = bridge.Version,
                placeId = pocket.PlaceId,
                stateData = new Dictionary<string, object> { { "slotIndex", slotIndex } },
            };
            UnityWebRequest req = ApiManager.Instance.UpdateItemRequest(bridge.Uuid, body);
            yield return req.SendWebRequest();
            if (req.responseCode < 200 || req.responseCode >= 300) {
                Debug.LogWarning($"[ServerItemManager] PickupToPocket PATCH failed code={req.responseCode} uuid={bridge.Uuid}");
                conn.Send(new S2C_PickupResult { Success = false, EntityId = entityId, ErrorMessage = "Objet introuvable" });
                yield break;
            }
            itemUuid = bridge.Uuid;
            version = bridge.Version + 1;
            try { var it = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text); if (it != null) version = it.version; } catch { }
        } else {
            string charId = conn.identity != null ? conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id : null;
            var body = new CreateItemBody {
                placeId = pocket.PlaceId, configId = entity.ItemConfigId, quantity = 1, ownedBy = charId,
                stateData = new Dictionary<string, object> { { "slotIndex", slotIndex } },
            };
            UnityWebRequest req = ApiManager.Instance.CreateItemRequest(body);
            yield return req.SendWebRequest();
            if (req.responseCode < 200 || req.responseCode >= 300) {
                conn.Send(new S2C_PickupResult { Success = false, EntityId = entityId, ErrorMessage = "Erreur stockage" });
                yield break;
            }
            try { var it = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text); if (it != null) { itemUuid = it.Id; version = it.version; } } catch { }
        }

        // Retire l'entité du monde en GARDANT sa ligne DB (l'item vit maintenant en poche).
        if (_rooms.TryGetValue(roomId, out var roomItems) && roomItems.Remove(entityId))
            BroadcastToRoom(roomId, new S2C_DestroyItem { EntityId = entityId, RoomId = roomId });
        _bridges.Remove(entityId);

        int pocketEntityId = _nextEntityId++;
        pocket.ItemsByEntityId[pocketEntityId] = new PocketSessionItem {
            EntityId = pocketEntityId, ConfigId = entity.ItemConfigId, SlotIndex = slotIndex,
            ItemUuid = itemUuid, Version = version, Quantity = 1,
        };
        pocket.SlotToEntityId[slotIndex] = pocketEntityId;
        if (!string.IsNullOrEmpty(itemUuid))
            _bridges[pocketEntityId] = new ItemDbBridge { Uuid = itemUuid, Version = version, PlaceId = pocket.PlaceId };

        SendPocketSnapshot(conn, pocket);
        conn.Send(new S2C_PickupResult { Success = true, EntityId = entityId });
        GameLogger.Network.Info("Item PickupToPocket netId={NetId} config={ConfigId} slot={Slot} uuid={Uuid}",
            conn.identity != null ? conn.identity.netId : 0, entity.ItemConfigId, slotIndex, itemUuid);
    }

    /// <summary>Crée un NOUVEL item directement dans la poche du joueur — spawn admin /
    /// récompense d'un item jamais équipable en main (munitions).</summary>
    private IEnumerator CreateItemInPocketCoroutine(NetworkConnectionToClient conn, int configId, string charId,
        PocketSession pocket, int slotIndex, System.Action onSuccess, System.Action<string> onFail) {
        var body = new CreateItemBody {
            placeId = pocket.PlaceId, configId = configId, quantity = 1, ownedBy = charId,
            stateData = new Dictionary<string, object> { { "slotIndex", slotIndex } },
        };
        UnityWebRequest req = ApiManager.Instance.CreateItemRequest(body);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300) { onFail?.Invoke("Erreur stockage"); yield break; }
        string itemUuid = null;
        int version = 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text); if (it != null) { itemUuid = it.Id; version = it.version; } } catch { }

        int entityId = _nextEntityId++;
        pocket.ItemsByEntityId[entityId] = new PocketSessionItem {
            EntityId = entityId, ConfigId = configId, SlotIndex = slotIndex,
            ItemUuid = itemUuid, Version = version, Quantity = 1,
        };
        pocket.SlotToEntityId[slotIndex] = entityId;
        if (!string.IsNullOrEmpty(itemUuid))
            _bridges[entityId] = new ItemDbBridge { Uuid = itemUuid, Version = version, PlaceId = pocket.PlaceId };
        SendPocketSnapshot(conn, pocket);
        GameLogger.Network.Info("Item SpawnInPocket netId={NetId} config={ConfigId} slot={Slot} uuid={Uuid}",
            conn.identity != null ? conn.identity.netId : 0, configId, slotIndex, itemUuid);
        onSuccess?.Invoke();
    }

    /// <summary>Chargeur à publier dans un snapshot d'inventaire (tooltip client) : -1 si
    /// l'item n'est pas une arme, sinon l'état mémoire (défaut plein, sans l'écrire).</summary>
    private static int SnapshotLoadedAmmo(int configId, string uuid) {
        if (!(DatabaseManager.GetItemConfigById(configId) is WeaponConfig cfg)) return -1;
        var mgr = Instance;
        if (mgr != null && !string.IsNullOrEmpty(uuid) && mgr._weaponAmmoByKey.TryGetValue(uuid, out int loaded)) return loaded;
        return cfg.MagazineSize;
    }

    /// <summary>Réhydrate le chargeur d'une arme depuis sa ligne DB (stateData.loadedAmmo) —
    /// appelé partout où des items sont (re)chargés depuis le backend. No-op si la session
    /// serveur connaît déjà cette arme (la mémoire fait foi) ou si la ligne n'a pas la clé
    /// (arme jamais tirée = chargeur plein par défaut).</summary>
    public void SeedWeaponAmmoFromDb(int configId, string uuid, Dictionary<string, object> stateData) {
        if (string.IsNullOrEmpty(uuid) || stateData == null) return;
        if (_weaponAmmoByKey.ContainsKey(uuid)) return;
        if (!(DatabaseManager.GetItemConfigById(configId) is WeaponConfig cfg)) return;
        if (!stateData.TryGetValue(StateKeyLoadedAmmo, out object raw) || raw == null) return;
        try {
            _weaponAmmoByKey[uuid] = Mathf.Clamp(System.Convert.ToInt32(raw), 0, cfg.MagazineSize);
        } catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] SeedWeaponAmmoFromDb parse uuid={uuid}: {e.Message}");
        }
    }

    // ── Montant des liasses de billets (cash ATM) ───────────────────────────────
    // Même machinerie que le chargeur d'arme : montant keyed "e:{entityId}" avant bridge DB
    // puis uuid (migré par AssociateUuid), persisté en stateData.cashAmount (débouncé),
    // réhydraté par SeedCashAmountFromDb partout où des items sont relus depuis le backend.
    // Le montant est fixé au retrait puis DÉCRÉMENTÉ par les dépôts partiels (TakeCashAmount
    // « fait la monnaie » dans les liasses du joueur) — jamais modifié autrement.

    private const string StateKeyCashAmount = "cashAmount";

    private readonly Dictionary<string, int> _cashAmountByKey = new Dictionary<string, int>();
    private readonly HashSet<string> _cashPersistPending = new HashSet<string>();

    /// <summary>Fixe le montant d'une liasse — entité MONDE (retrait ATM, décrément d'une
    /// liasse tenue lors d'un dépôt partiel) : mémoire serveur + persistance stateData +
    /// réplication S2C_ItemCash aux clients de la room.</summary>
    public void SetCashAmount(string roomId, int entityId, int amount) {
        ItemDbBridge bridge = GetBridge(entityId);
        string key = bridge != null && !string.IsNullOrEmpty(bridge.Uuid) ? bridge.Uuid : "e:" + entityId;
        _cashAmountByKey[key] = amount;
        QueueCashPersist(entityId);
        BroadcastToRoom(roomId, new S2C_ItemCash { EntityId = entityId, Amount = amount });
    }

    /// <summary>Montant de la liasse <paramref name="entityId"/> (0 si inconnue).</summary>
    public int GetCashAmount(int entityId) {
        ItemDbBridge bridge = GetBridge(entityId);
        if (bridge != null && !string.IsNullOrEmpty(bridge.Uuid)
            && _cashAmountByKey.TryGetValue(bridge.Uuid, out int byUuid)) return byUuid;
        return _cashAmountByKey.TryGetValue("e:" + entityId, out int byEntity) ? byEntity : 0;
    }

    /// <summary>Retrait ATM : crée une liasse du montant donné directement dans la POCHE du
    /// joueur — règle projet : un item donné au joueur passe par l'inventaire, jamais par
    /// les mains. Le montant part dans le POST backend (stateData.cashAmount — pas de
    /// persistance différée à risquer) puis est mémorisé par uuid avant le re-snapshot poche
    /// (tooltip + total « Sur vous » de l'ATM à jour immédiatement).
    /// <paramref name="onFail"/> reçoit un message de toast (poche pleine, erreur backend).</summary>
    public void SpawnCashInPocket(NetworkConnectionToClient conn, int amount,
        System.Action onSuccess, System.Action<string> onFail) {
        if (conn?.identity == null || amount <= 0) { onFail?.Invoke("Service indisponible"); return; }

        // Anti-doublon : s'il y a DÉJÀ une liasse en poche, le retrait s'y ADDITIONNE au
        // lieu de créer un second item cash (une seule liasse par joueur en pratique).
        if (_pocketByPlayer.TryGetValue(conn.identity.netId, out PocketSession existing)) {
            foreach (PocketSessionItem pi in existing.ItemsByEntityId.Values) {
                if (pi.ConfigId != CashItem.ConfigId || string.IsNullOrEmpty(pi.ItemUuid)) continue;
                _cashAmountByKey.TryGetValue(pi.ItemUuid, out int current);
                _cashAmountByKey[pi.ItemUuid] = current + amount;
                QueueCashPersist(pi.EntityId);
                SendPocketSnapshot(conn, existing);
                GameLogger.Network.Info("CashMergeOnWithdraw netId={NetId} amount={Amount} uuid={Uuid} total={Total}",
                    conn.identity.netId, amount, pi.ItemUuid, current + amount);
                onSuccess?.Invoke();
                return;
            }
        }

        if (!TryGetFreePocketSlot(conn.identity.netId, out PocketSession pocket, out int slot)) {
            onFail?.Invoke(InventoryToasts.NoSpaceInInventory);
            return;
        }
        string charId = conn.identity.GetComponent<Sim.PlayerController>()?.CharacterData?.Id;
        ApiManager.Instance?.StartCoroutine(
            SpawnCashInPocketCoroutine(conn, charId, pocket, slot, amount, onSuccess, onFail));
    }

    // Variante cash de CreateItemInPocketCoroutine : stateData.cashAmount écrit à la création
    // et montant publié dans le snapshot (SnapshotCashAmount lit _cashAmountByKey[uuid]).
    private IEnumerator SpawnCashInPocketCoroutine(NetworkConnectionToClient conn, string charId,
        PocketSession pocket, int slotIndex, int amount, System.Action onSuccess, System.Action<string> onFail) {
        var body = new CreateItemBody {
            placeId = pocket.PlaceId, configId = CashItem.ConfigId, quantity = 1, ownedBy = charId,
            stateData = new Dictionary<string, object> {
                { "slotIndex", slotIndex },
                { StateKeyCashAmount, amount },
            },
        };
        UnityWebRequest req = ApiManager.Instance.CreateItemRequest(body);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300) { onFail?.Invoke("Service indisponible"); yield break; }
        string itemUuid = null;
        int version = 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text); if (it != null) { itemUuid = it.Id; version = it.version; } } catch { }

        int entityId = _nextEntityId++;
        pocket.ItemsByEntityId[entityId] = new PocketSessionItem {
            EntityId = entityId, ConfigId = CashItem.ConfigId, SlotIndex = slotIndex,
            ItemUuid = itemUuid, Version = version, Quantity = 1,
        };
        pocket.SlotToEntityId[slotIndex] = entityId;
        if (!string.IsNullOrEmpty(itemUuid)) {
            _bridges[entityId] = new ItemDbBridge { Uuid = itemUuid, Version = version, PlaceId = pocket.PlaceId };
            _cashAmountByKey[itemUuid] = amount;
        }
        SendPocketSnapshot(conn, pocket);
        GameLogger.Network.Info("CashSpawnInPocket netId={NetId} amount={Amount} slot={Slot} uuid={Uuid}",
            conn.identity != null ? conn.identity.netId : 0, amount, slotIndex, itemUuid);
        onSuccess?.Invoke();
    }

    /// <summary>La liasse tenue en main par le joueur (vraie prise OU projection hotbar),
    /// avec sa room et son montant. Faux si aucune liasse en main.</summary>
    public bool TryGetHeldCash(NetworkConnectionToClient conn, out string roomId, out int entityId, out int amount) {
        roomId = null; entityId = -1; amount = 0;
        if (conn?.identity == null) return false;
        roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null || !_playerHands.TryGetValue(conn.identity.netId, out var hands)) return false;

        int[] held = { hands.RightEntityId, hands.LeftEntityId };
        foreach (int eid in held) {
            if (eid == -1 || !TryGetEntity(roomId, eid, out var e)) continue;
            if (e.ItemConfigId != CashItem.ConfigId) continue;
            entityId = eid;
            amount = GetCashAmount(eid);
            return true;
        }
        return false;
    }

    /// <summary>Dépôt ATM : retire la liasse tenue (entité monde + ligne DB — projection
    /// hotbar incluse, <see cref="DespawnItem"/> purge aussi l'entrée poche) et renvoie son
    /// montant. Faux si aucune liasse en main ou montant inconnu (0).</summary>
    private bool TakeHeldCash(NetworkConnectionToClient conn, out int amount) {
        if (!TryGetHeldCash(conn, out string roomId, out int entityId, out amount) || amount <= 0) return false;
        ItemDbBridge bridge = GetBridge(entityId);
        if (bridge != null && !string.IsNullOrEmpty(bridge.Uuid)) _cashAmountByKey.Remove(bridge.Uuid);
        _cashAmountByKey.Remove("e:" + entityId);
        DespawnItem(roomId, entityId);
        return true;
    }

    /// <summary>Une liasse mobilisable pour un dépôt ATM : soit l'entité monde TENUE (vraie
    /// prise ou projection hotbar), soit une ligne de la session poche.</summary>
    private sealed class CashSource {
        public bool   Held;
        public int    EntityId;   // entité monde (main) ou entité de session poche
        public string RoomId;     // main uniquement
        public PocketSessionItem Pocket;
        public string Uuid;
        public int    Amount;
    }

    /// <summary>Dépôt ATM : assemble <paramref name="amount"/> depuis les liasses SUR le
    /// joueur (main + poche, la projection hotbar dédupliquée par uuid). Façon caissier :
    /// liasse au montant exact d'abord, sinon consommation des plus grosses liasses
    /// inférieures au restant (supprimées), et au plus UNE liasse décrémentée à la fin —
    /// le dépôt ne fragmente jamais plus d'une liasse. <paramref name="done"/>(couvert,
    /// montant réellement pris) — pris &lt; demandé seulement sur erreur backend partielle
    /// (le reste est laissé au joueur).</summary>
    public void TakeCashAmount(NetworkConnectionToClient conn, int amount, System.Action<bool, int> done) {
        if (amount <= 0 || conn?.identity == null || ApiManager.Instance == null) { done(false, 0); return; }
        ApiManager.Instance.StartCoroutine(TakeCashAmountCoroutine(conn, amount, done));
    }

    private IEnumerator TakeCashAmountCoroutine(NetworkConnectionToClient conn, int amount, System.Action<bool, int> done) {
        uint netId = conn.identity.netId;

        // Inventaire cash : main d'abord, puis poche — la liasse projetée en main par la
        // hotbar EST une ligne de poche (bridge partagé) : dédupliquée par uuid.
        var sources = new List<CashSource>();
        string heldUuid = null;
        if (TryGetHeldCash(conn, out string heldRoomId, out int heldEntityId, out int heldAmount) && heldAmount > 0) {
            heldUuid = GetBridge(heldEntityId)?.Uuid;
            sources.Add(new CashSource { Held = true, EntityId = heldEntityId, RoomId = heldRoomId, Amount = heldAmount, Uuid = heldUuid });
        }
        if (_pocketByPlayer.TryGetValue(netId, out var pocket)) {
            foreach (var it in pocket.ItemsByEntityId.Values) {
                if (it.ConfigId != CashItem.ConfigId) continue;
                if (!string.IsNullOrEmpty(heldUuid) && it.ItemUuid == heldUuid) continue;
                if (!_cashAmountByKey.TryGetValue(it.ItemUuid, out int a) || a <= 0) continue;
                sources.Add(new CashSource { EntityId = it.EntityId, Pocket = it, Uuid = it.ItemUuid, Amount = a });
            }
        }

        int total = 0;
        foreach (var s in sources) total += s.Amount;
        if (total < amount) { done(false, 0); yield break; }

        int remaining = amount;
        bool touchedPocket = false;
        while (remaining > 0 && sources.Count > 0) {
            // Choix caissier : montant exact > la plus grosse liasse < restant (supprimée
            // entière) > la plus petite liasse > restant (décrémentée, fin du dépôt).
            CashSource pick = null;
            foreach (var s in sources) if (s.Amount == remaining) { pick = s; break; }
            if (pick == null)
                foreach (var s in sources)
                    if (s.Amount < remaining && (pick == null || s.Amount > pick.Amount)) pick = s;
            if (pick == null)
                foreach (var s in sources)
                    if (pick == null || s.Amount < pick.Amount) pick = s;

            sources.Remove(pick);

            if (pick.Amount > remaining) {
                // Décrément : main = SetCashAmount (persist + S2C_ItemCash) ; poche = mémoire
                // + persist débouncé (le snapshot poche renvoyé en fin de flux porte le montant).
                if (pick.Held) {
                    SetCashAmount(pick.RoomId, pick.EntityId, pick.Amount - remaining);
                } else {
                    _cashAmountByKey[pick.Uuid] = pick.Amount - remaining;
                    QueueCashPersist(pick.EntityId);
                }
                // La liasse tenue peut être une projection hotbar (= ligne de poche) :
                // on rafraîchit la poche dans tous les cas, superflu sans effet sinon.
                touchedPocket = true;
                remaining = 0;
            } else if (pick.Held) {
                if (TakeHeldCash(conn, out int takenHeld)) {
                    remaining -= takenHeld;
                    touchedPocket = true; // projection : DespawnItem purge aussi l'entrée poche
                }
            } else {
                UnityWebRequest del = ApiManager.Instance.DeleteItemRequest(pick.Uuid);
                yield return del.SendWebRequest();
                if (del.responseCode < 200 || del.responseCode >= 300) {
                    // Ligne morte/désynchronisée : la liasse reste au joueur, on continue
                    // avec les autres (le crédit final = ce qui a réellement été pris).
                    Debug.LogWarning($"[ServerItemManager] TakeCashAmount DELETE failed code={del.responseCode} uuid={pick.Uuid}");
                    continue;
                }
                if (_pocketByPlayer.TryGetValue(netId, out var p)) {
                    p.ItemsByEntityId.Remove(pick.EntityId);
                    p.SlotToEntityId.Remove(pick.Pocket.SlotIndex);
                }
                _bridges.Remove(pick.EntityId);
                _cashAmountByKey.Remove(pick.Uuid);
                remaining -= pick.Amount;
                touchedPocket = true;
            }
        }

        if (touchedPocket && _pocketByPlayer.TryGetValue(netId, out var refreshed))
            SendPocketSnapshot(conn, refreshed);

        done(remaining == 0, amount - remaining);
    }

    /// <summary>Action « Tout fusionner » de la liasse : fusionne TOUTES les liasses du
    /// joueur (main + poche, projection hotbar dédupliquée par uuid) en UNE seule — la
    /// cible désignée par le client (<see cref="C2S_MergeAllCash.TargetEntityId"/>), repli
    /// sur la plus grosse. Les montants ne sont crédités qu'une fois chaque suppression
    /// backend confirmée (pattern TakeCashAmount) — pas de duplication possible ; une
    /// liasse dont le DELETE échoue reste simplement au joueur.</summary>
    public void HandleMergeAllCash(NetworkConnectionToClient conn, C2S_MergeAllCash msg) {
        if (conn?.identity == null || ApiManager.Instance == null) return;
        ApiManager.Instance.StartCoroutine(MergeAllCashCoroutine(conn, msg.TargetEntityId));
    }

    private IEnumerator MergeAllCashCoroutine(NetworkConnectionToClient conn, int targetEntityId) {
        uint netId = conn.identity.netId;

        // Même collecte que TakeCashAmount : main d'abord puis poche, dédup projection.
        var sources = new List<CashSource>();
        string heldUuid = null;
        if (TryGetHeldCash(conn, out string heldRoomId, out int heldEntityId, out int heldAmount) && heldAmount > 0) {
            heldUuid = GetBridge(heldEntityId)?.Uuid;
            sources.Add(new CashSource { Held = true, EntityId = heldEntityId, RoomId = heldRoomId, Amount = heldAmount, Uuid = heldUuid });
        }
        if (_pocketByPlayer.TryGetValue(netId, out var pocket)) {
            foreach (var it in pocket.ItemsByEntityId.Values) {
                if (it.ConfigId != CashItem.ConfigId) continue;
                if (!string.IsNullOrEmpty(heldUuid) && it.ItemUuid == heldUuid) continue;
                if (!_cashAmountByKey.TryGetValue(it.ItemUuid, out int a) || a <= 0) continue;
                sources.Add(new CashSource { EntityId = it.EntityId, Pocket = it, Uuid = it.ItemUuid, Amount = a });
            }
        }
        if (sources.Count < 2) {
            conn.Send(new ToastNotificationMessage {
                text = "Rien à fusionner", worldToast = true, kindByte = (byte)ToastKind.Error,
            });
            yield break;
        }

        CashSource target = null;
        foreach (var s in sources) if (s.EntityId == targetEntityId) { target = s; break; }
        if (target == null)
            foreach (var s in sources) if (target == null || s.Amount > target.Amount) target = s;

        int merged = 0;
        foreach (var src in sources) {
            if (src == target) continue;
            if (src.Held) {
                if (TakeHeldCash(conn, out int takenHeld)) merged += takenHeld;
                continue;
            }
            UnityWebRequest del = ApiManager.Instance.DeleteItemRequest(src.Uuid);
            yield return del.SendWebRequest();
            if (del.responseCode < 200 || del.responseCode >= 300) {
                Debug.LogWarning($"[ServerItemManager] MergeAllCash DELETE failed code={del.responseCode} uuid={src.Uuid} — liasse laissée au joueur");
                continue;
            }
            if (_pocketByPlayer.TryGetValue(netId, out var p)) {
                p.ItemsByEntityId.Remove(src.EntityId);
                p.SlotToEntityId.Remove(src.Pocket.SlotIndex);
            }
            _bridges.Remove(src.EntityId);
            _cashAmountByKey.Remove(src.Uuid);
            merged += src.Amount;
        }

        if (merged > 0) {
            if (target.Held) {
                // Main (vraie prise ou projection hotbar) : persist + S2C_ItemCash.
                SetCashAmount(target.RoomId, target.EntityId, target.Amount + merged);
            } else {
                _cashAmountByKey[target.Uuid] = target.Amount + merged;
                QueueCashPersist(target.EntityId);
            }
        }
        if (_pocketByPlayer.TryGetValue(netId, out var refreshedPocket))
            SendPocketSnapshot(conn, refreshedPocket);
        GameLogger.Network.Info("CashMergeAll netId={NetId} merged={Merged} total={Total} tgtUuid={Uuid}",
            netId, merged, target.Amount + merged, target.Uuid);
    }

    /// <summary>Montant de liasse à publier dans un snapshot de poche (tooltip client +
    /// gating du dépôt ATM) : 0 si l'item n'est pas une liasse ou montant inconnu.</summary>
    private static int SnapshotCashAmount(int configId, string uuid) {
        if (configId != CashItem.ConfigId) return 0;
        var mgr = Instance;
        return mgr != null && !string.IsNullOrEmpty(uuid)
            && mgr._cashAmountByKey.TryGetValue(uuid, out int amount) ? amount : 0;
    }

    private void QueueCashPersist(int cashEntityId) {
        ItemDbBridge bridge = GetBridge(cashEntityId);
        // Pas encore de ligne DB (persist pickup asynchrone) : AssociateUuid re-déclenchera
        // cette écriture à la création du bridge.
        if (bridge == null || string.IsNullOrEmpty(bridge.Uuid) || ApiManager.Instance == null) return;
        if (!_cashPersistPending.Add(bridge.Uuid)) return; // écriture déjà programmée
        ApiManager.Instance.StartCoroutine(PersistCashAmountCoroutine(bridge.Uuid));
    }

    private IEnumerator PersistCashAmountCoroutine(string uuid) {
        yield return new WaitForSeconds(AmmoPersistDebounce);
        _cashPersistPending.Remove(uuid);
        if (!_cashAmountByKey.TryGetValue(uuid, out int amount)) yield break;

        // Bridge re-résolu PAR UUID au moment de l'écriture (même garde que le chargeur).
        if (!TryGetBridgeByUuid(uuid, out int entityId, out ItemDbBridge bridge)) yield break;

        var state = new Dictionary<string, object> { { StateKeyCashAmount, amount } };
        if (TryGetSlotIndexByUuid(uuid, out int slotIndex)) state["slotIndex"] = slotIndex;

        var body = new UpdateItemBody { expectedVersion = bridge.Version, stateData = state };
        UnityWebRequest req = ApiManager.Instance.UpdateItemRequest(uuid, body);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] PersistCashAmount failed code={req.responseCode} uuid={uuid}");
            yield break;
        }
        try {
            var it = JsonConvert.DeserializeObject<ItemJson>(req.downloadHandler.text);
            if (it != null) UpdateBridgeVersion(entityId, it.version);
        } catch { }
    }

    /// <summary>Réhydrate le montant d'une liasse depuis sa ligne DB (stateData.cashAmount).
    /// No-op si la session serveur la connaît déjà (la mémoire fait foi).</summary>
    public void SeedCashAmountFromDb(int configId, string uuid, Dictionary<string, object> stateData) {
        if (configId != CashItem.ConfigId || string.IsNullOrEmpty(uuid) || stateData == null) return;
        if (_cashAmountByKey.ContainsKey(uuid)) return;
        if (!stateData.TryGetValue(StateKeyCashAmount, out object raw) || raw == null) return;
        try {
            _cashAmountByKey[uuid] = Mathf.Max(0, System.Convert.ToInt32(raw));
        } catch (System.Exception e) {
            Debug.LogWarning($"[ServerItemManager] SeedCashAmountFromDb parse uuid={uuid}: {e.Message}");
        }
    }

    /// <summary>Réhydratation groupée de l'état applicatif d'un item relu depuis le backend
    /// (chargeur d'arme + montant de liasse) — point d'appel unique des restores.</summary>
    private void SeedItemStateFromDb(int configId, string uuid, Dictionary<string, object> stateData) {
        SeedWeaponAmmoFromDb(configId, uuid, stateData);
        SeedCashAmountFromDb(configId, uuid, stateData);
    }

    /// <summary>stateData d'un PATCH de déplacement d'inventaire. Le backend REMPLACE le
    /// dict : une destination à slotIndex doit donc réinjecter les clés applicatives qui
    /// survivent au move — le chargeur d'une arme suivie. Une destination SANS slotIndex
    /// (main) renvoie null : champ omis du PATCH, stateData DB préservé tel quel.</summary>
    private Dictionary<string, object> MoveStateData(int configId, string uuid, bool hasSlotIndex, int slotIndex) {
        if (!hasSlotIndex) return null;
        var state = new Dictionary<string, object> { { "slotIndex", slotIndex } };
        if (!string.IsNullOrEmpty(uuid)
            && DatabaseManager.GetItemConfigById(configId) is WeaponConfig
            && _weaponAmmoByKey.TryGetValue(uuid, out int loaded)) {
            state[StateKeyLoadedAmmo] = loaded;
        }
        // Liasse de billets : réinjecte son montant (le PATCH remplace stateData intégralement).
        if (configId == CashItem.ConfigId && !string.IsNullOrEmpty(uuid)
            && _cashAmountByKey.TryGetValue(uuid, out int cashAmount)) {
            state[StateKeyCashAmount] = cashAmount;
        }
        return state;
    }

    /// <summary>
    /// Consomme UNE boîte de munitions de la famille <paramref name="category"/> dans
    /// l'inventaire du joueur : sa POCHE d'abord (session déjà en mémoire), puis un
    /// SAC/COLIS tenu en main (contenu lu en DB — stacks décrémentés). Asynchrone
    /// (aller-retour backend) ; <paramref name="done"/>(succès, motif d'échec FR).
    /// Le chargeur lui-même appartient à l'appelant (PlayerShooter) — ici on ne
    /// détruit que l'item consommé et on re-pousse les snapshots d'UI concernés.
    /// </summary>
    public void ConsumeAmmoItem(NetworkConnectionToClient conn, AmmoCategory category, System.Action<bool, string> done) {
        if (conn?.identity == null || ApiManager.Instance == null) { done(false, "Pas de joueur"); return; }
        ApiManager.Instance.StartCoroutine(ConsumeAmmoCoroutine(conn, category, done));
    }

    private IEnumerator ConsumeAmmoCoroutine(NetworkConnectionToClient conn, AmmoCategory category, System.Action<bool, string> done) {
        uint netId = conn.identity.netId;

        // 1) POCHE : jamais de stack (invariant 1 item/slot) → DELETE sec.
        if (_pocketByPlayer.TryGetValue(netId, out var pocket)) {
            PocketSessionItem found = null;
            foreach (var it in pocket.ItemsByEntityId.Values) {
                if (DatabaseManager.GetItemConfigById(it.ConfigId) is AmmoConfig ammo && ammo.AmmoCategory == category) {
                    found = it;
                    break;
                }
            }
            if (found != null) {
                UnityWebRequest del = ApiManager.Instance.DeleteItemRequest(found.ItemUuid);
                yield return del.SendWebRequest();
                if (del.responseCode >= 200 && del.responseCode < 300) {
                    pocket.ItemsByEntityId.Remove(found.EntityId);
                    pocket.SlotToEntityId.Remove(found.SlotIndex);
                    _bridges.Remove(found.EntityId);
                    SendPocketSnapshot(conn, pocket);
                    GameLogger.Network.Info("AmmoConsumed netId={NetId} category={Category} source=pocket uuid={Uuid}",
                        netId, category, found.ItemUuid);
                    done(true, null);
                    yield break;
                }
                // Ligne morte/désynchronisée (404/409) : on tente encore le sac tenu.
                Debug.LogWarning($"[ServerItemManager] ConsumeAmmo pocket DELETE failed code={del.responseCode} uuid={found.ItemUuid}");
            }
        }

        // 2) SAC/COLIS TENU EN MAIN.
        string roomId = PlayerRoomTracker.Instance != null ? PlayerRoomTracker.Instance.GetRoom(conn) : null;
        if (roomId != null && TryGetHeldPackage(netId, roomId, out _, out string pkgUuid, out _)) {
            // 2a) Sa session est OUVERTE (UI affichée) : versions OCC déjà en mémoire.
            if (_openItemContainerByPlayer.TryGetValue(netId, out var ics) && ics.PackageItemUuid == pkgUuid) {
                ContainerSessionItem entry = null;
                foreach (var it in ics.ItemsByEntityId.Values) {
                    if (!it.IsProp && DatabaseManager.GetItemConfigById(it.ConfigId) is AmmoConfig ammo
                        && ammo.AmmoCategory == category) {
                        entry = it;
                        break;
                    }
                }
                if (entry == null) { done(false, "Aucune munition compatible sur vous"); yield break; }

                int qty = Mathf.Max(1, entry.Quantity);
                bool rowOk = false; int newVersion = entry.Version;
                yield return ConsumeAmmoRow(entry.ItemUuid, entry.Version, qty, (ok, v) => { rowOk = ok; newVersion = v; });
                if (!rowOk) { done(false, "Munitions introuvables (réessayez)"); yield break; }

                if (qty > 1) {
                    entry.Quantity = qty - 1;
                    entry.Version = newVersion;
                    if (_bridges.TryGetValue(entry.EntityId, out var br)) br.Version = newVersion;
                } else {
                    ics.ItemsByEntityId.Remove(entry.EntityId);
                    ics.SlotToEntityId.Remove(entry.SlotIndex);
                    _bridges.Remove(entry.EntityId);
                }
                SendItemContainerSnapshot(conn, ics);
                GameLogger.Network.Info("AmmoConsumed netId={NetId} category={Category} source=openBag uuid={Uuid}",
                    netId, category, entry.ItemUuid);
                done(true, null);
                yield break;
            }

            // 2b) Sac fermé : contenu lu en DB (place item_container:{uuid}, créée au besoin).
            var createBody = new CreatePlaceBody { placeKey = "item_container:" + pkgUuid, type = "container", ownerId = pkgUuid };
            UnityWebRequest createReq = ApiManager.Instance.CreatePlaceRequest(createBody);
            yield return createReq.SendWebRequest();
            string placeId = null;
            if (createReq.responseCode >= 200 && createReq.responseCode < 300) {
                try { placeId = JsonConvert.DeserializeObject<PlaceJson>(createReq.downloadHandler.text)?.Id; } catch { }
            }
            if (!string.IsNullOrEmpty(placeId)) {
                UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
                yield return stateReq.SendWebRequest();
                ItemJson row = null;
                if (stateReq.responseCode == 200) {
                    try {
                        var state = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text);
                        if (state?.items != null) {
                            foreach (var it in state.items) {
                                if (DatabaseManager.GetItemConfigById(it.configId) is AmmoConfig ammo
                                    && ammo.AmmoCategory == category) {
                                    row = it;
                                    break;
                                }
                            }
                        }
                    } catch (System.Exception e) {
                        Debug.LogWarning($"[ServerItemManager] ConsumeAmmo bag state parse: {e.Message}");
                    }
                }
                if (row != null) {
                    bool rowOk = false;
                    yield return ConsumeAmmoRow(row.Id, row.version, Mathf.Max(1, row.quantity), (ok, _) => rowOk = ok);
                    if (rowOk) {
                        GameLogger.Network.Info("AmmoConsumed netId={NetId} category={Category} source=bag uuid={Uuid}",
                            netId, category, row.Id);
                        done(true, null);
                        yield break;
                    }
                }
            }
        }

        done(false, "Aucune munition compatible sur vous");
    }

    /// <summary>Compte les BALLES de réserve du joueur pour l'arme donnée : boîtes de
    /// munitions compatibles dans la poche + le sac/colis tenu (exactement ce que le
    /// rechargement peut consommer), une boîte valant un chargeur plein (magazineSize).
    /// Async (un sac fermé se lit en DB) ; <paramref name="done"/>(balles). Affichage HUD
    /// uniquement — la consommation réelle revalide tout au moment du reload.</summary>
    public void CountAmmoReserve(NetworkConnectionToClient conn, WeaponConfig weapon, System.Action<int> done) {
        if (conn?.identity == null || ApiManager.Instance == null || weapon == null) { done(0); return; }
        ApiManager.Instance.StartCoroutine(CountAmmoReserveCoroutine(conn, weapon, done));
    }

    private IEnumerator CountAmmoReserveCoroutine(NetworkConnectionToClient conn, WeaponConfig weapon, System.Action<int> done) {
        uint netId = conn.identity.netId;
        int boxes = 0;

        // Poche (session en mémoire).
        if (_pocketByPlayer.TryGetValue(netId, out var pocket)) {
            foreach (var it in pocket.ItemsByEntityId.Values) {
                if (DatabaseManager.GetItemConfigById(it.ConfigId) is AmmoConfig ammo
                    && ammo.AmmoCategory == weapon.AmmoCategory) boxes += Mathf.Max(1, it.Quantity);
            }
        }

        // Sac/colis tenu : session ouverte = comptage mémoire, sinon lecture DB.
        string roomId = PlayerRoomTracker.Instance != null ? PlayerRoomTracker.Instance.GetRoom(conn) : null;
        if (roomId != null && TryGetHeldPackage(netId, roomId, out _, out string pkgUuid, out _)) {
            if (_openItemContainerByPlayer.TryGetValue(netId, out var ics) && ics.PackageItemUuid == pkgUuid) {
                foreach (var it in ics.ItemsByEntityId.Values) {
                    if (!it.IsProp && DatabaseManager.GetItemConfigById(it.ConfigId) is AmmoConfig ammo
                        && ammo.AmmoCategory == weapon.AmmoCategory) boxes += Mathf.Max(1, it.Quantity);
                }
            } else {
                var createBody = new CreatePlaceBody { placeKey = "item_container:" + pkgUuid, type = "container", ownerId = pkgUuid };
                UnityWebRequest createReq = ApiManager.Instance.CreatePlaceRequest(createBody);
                yield return createReq.SendWebRequest();
                string placeId = null;
                if (createReq.responseCode >= 200 && createReq.responseCode < 300) {
                    try { placeId = JsonConvert.DeserializeObject<PlaceJson>(createReq.downloadHandler.text)?.Id; } catch { }
                }
                if (!string.IsNullOrEmpty(placeId)) {
                    UnityWebRequest stateReq = ApiManager.Instance.GetPlaceStateRequest(placeId);
                    yield return stateReq.SendWebRequest();
                    if (stateReq.responseCode == 200) {
                        try {
                            var state = JsonConvert.DeserializeObject<PlaceStateJson>(stateReq.downloadHandler.text);
                            if (state?.items != null) {
                                foreach (var it in state.items) {
                                    if (DatabaseManager.GetItemConfigById(it.configId) is AmmoConfig ammo
                                        && ammo.AmmoCategory == weapon.AmmoCategory) boxes += Mathf.Max(1, it.quantity);
                                }
                            }
                        } catch { }
                    }
                }
            }
        }

        done(boxes * weapon.MagazineSize);
    }

    /// <summary>Retire UNE unité de la ligne d'item munitions : stack → PATCH quantity-1
    /// (OCC), dernière unité → DELETE. <paramref name="cb"/>(ok, nouvelle version OCC).</summary>
    private IEnumerator ConsumeAmmoRow(string uuid, int version, int quantity, System.Action<bool, int> cb) {
        if (quantity > 1) {
            var body = new UpdateItemBody { expectedVersion = version, quantity = quantity - 1 };
            UnityWebRequest patch = ApiManager.Instance.UpdateItemRequest(uuid, body);
            yield return patch.SendWebRequest();
            if (patch.responseCode < 200 || patch.responseCode >= 300) {
                Debug.LogWarning($"[ServerItemManager] ConsumeAmmoRow PATCH failed code={patch.responseCode} uuid={uuid}");
                cb(false, version);
                yield break;
            }
            int newVersion = version + 1;
            try { var it = JsonConvert.DeserializeObject<ItemJson>(patch.downloadHandler.text); if (it != null) newVersion = it.version; } catch { }
            cb(true, newVersion);
        } else {
            UnityWebRequest del = ApiManager.Instance.DeleteItemRequest(uuid);
            yield return del.SendWebRequest();
            bool ok = del.responseCode >= 200 && del.responseCode < 300;
            if (!ok) Debug.LogWarning($"[ServerItemManager] ConsumeAmmoRow DELETE failed code={del.responseCode} uuid={uuid}");
            cb(ok, version);
        }
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

        // La projection hotbar ne survit pas au changement de room : le client re-pointe
        // de lui-même (ReconcilePointer) une fois la nouvelle room snapshotée.
        _handProjection.Remove(playerNetId);

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
                // Phase 3 : la main gauche n'est plus un slot logique. Les items encore
                // persistés en hand_left (dual-carry d'avant migration) partent en poche.
                yield return MigrateLeftHandToPocket(conn, playerNetId, inv);
                yield return RestoreOneHand(conn, roomId, playerNetId, inv.HandRightPlaceId, HandType.Right);
                yield return RestoreOneHand(conn, roomId, playerNetId, inv.BackpackPlaceId,  HandType.Back);
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
            _handProjection.Remove(playerNetId);
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

    /// <summary>
    /// Migration « un item actif » : vide la place hand_left (dual-carry d'avant la
    /// phase 3) vers des slots de poche libres, par PATCH DB direct. Lancée au connect
    /// avant la restauration de la main droite ; si une session poche existe, l'entrée
    /// migrée y est insérée et le snapshot re-poussé. Poche pleine → l'item reste en
    /// hand_left (retenté au prochain connect ; la place n'est plus jamais assignée).
    /// </summary>
    private IEnumerator MigrateLeftHandToPocket(NetworkConnectionToClient conn, uint playerNetId, PlayerInventory inv) {
        if (string.IsNullOrEmpty(inv.HandLeftPlaceId) || string.IsNullOrEmpty(inv.PocketPlaceId)) yield break;

        UnityWebRequest leftReq = ApiManager.Instance.GetPlaceStateRequest(inv.HandLeftPlaceId);
        yield return leftReq.SendWebRequest();
        if (leftReq.responseCode != 200) yield break;

        PlaceStateJson leftState;
        try { leftState = JsonConvert.DeserializeObject<PlaceStateJson>(leftReq.downloadHandler.text); }
        catch (System.Exception e) { Debug.LogWarning($"[ServerItemManager] MigrateLeftHand parse error: {e.Message}"); yield break; }
        if (leftState?.items == null || leftState.items.Length == 0) yield break;

        // Slots occupés lus depuis la DB : la session poche n'est pas forcément hydratée.
        UnityWebRequest pocketReq = ApiManager.Instance.GetPlaceStateRequest(inv.PocketPlaceId);
        yield return pocketReq.SendWebRequest();
        if (pocketReq.responseCode != 200) yield break;

        var occupied = new HashSet<int>();
        try {
            PlaceStateJson pocketState = JsonConvert.DeserializeObject<PlaceStateJson>(pocketReq.downloadHandler.text);
            if (pocketState?.items != null)
                foreach (ItemJson pocketIt in pocketState.items) occupied.Add(ReadSlotIndex(pocketIt.stateData));
        } catch (System.Exception e) { Debug.LogWarning($"[ServerItemManager] MigrateLeftHand pocket parse error: {e.Message}"); yield break; }

        foreach (ItemJson it in leftState.items) {
            int freeSlot = -1;
            for (int i = 0; i < PocketCapacity; i++) if (!occupied.Contains(i)) { freeSlot = i; break; }
            if (freeSlot < 0) {
                GameLogger.Network.Warning("Item MigrateLeftHand poche pleine — uuid={Uuid} laissé en hand_left", it.Id);
                break;
            }

            var body = new UpdateItemBody {
                expectedVersion = it.version,
                placeId = inv.PocketPlaceId,
                stateData = new Dictionary<string, object> { { "slotIndex", freeSlot } },
            };
            UnityWebRequest patch = ApiManager.Instance.UpdateItemRequest(it.Id, body);
            yield return patch.SendWebRequest();
            if (patch.responseCode < 200 || patch.responseCode >= 300) {
                Debug.LogWarning($"[ServerItemManager] MigrateLeftHand PATCH failed code={patch.responseCode} uuid={it.Id}");
                continue;
            }
            occupied.Add(freeSlot);
            int newVersion = it.version + 1;
            try { var updated = JsonConvert.DeserializeObject<ItemJson>(patch.downloadHandler.text); if (updated != null) newVersion = updated.version; } catch { }

            // Session poche déjà hydratée (re-lookup à chaque itération : EnsurePocketSession
            // peut aboutir pendant nos awaits) → reflète la migration en mémoire + snapshot.
            if (_pocketByPlayer.TryGetValue(playerNetId, out PocketSession session)) {
                int pocketEntityId = _nextEntityId++;
                session.ItemsByEntityId[pocketEntityId] = new PocketSessionItem {
                    EntityId = pocketEntityId, ConfigId = it.configId, SlotIndex = freeSlot,
                    ItemUuid = it.Id, Version = newVersion, Quantity = 1,
                };
                session.SlotToEntityId[freeSlot] = pocketEntityId;
                _bridges[pocketEntityId] = new ItemDbBridge { Uuid = it.Id, Version = newVersion, PlaceId = session.PlaceId };
                SendPocketSnapshot(conn, session);
            }

            GameLogger.Network.Info("Item MigrateLeftHand uuid={Uuid} config={ConfigId} → pocket slot={Slot}", it.Id, it.configId, freeSlot);
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

            // Arme restaurée en main : réhydrate son chargeur depuis stateData.loadedAmmo.
            SeedItemStateFromDb(it.configId, it.Id, it.stateData);

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

            // Liasse restaurée en main : pousse son montant (même double envoi que le spawn —
            // le montant vient d'être réhydraté par SeedItemStateFromDb, résolu par uuid).
            if (it.configId == CashItem.ConfigId) {
                var cashMsg = new S2C_ItemCash { EntityId = entityId, Amount = GetCashAmount(entityId) };
                if (conn != null && conn.isReady) conn.Send(cashMsg);
                BroadcastToRoom(liveRoom, cashMsg);
            }

            GameLogger.Network.Info("Item Restore entity={EntityId} configId={ItemConfigId} player={PlayerNetId} hand={Hand} room={Room}",
                entityId, it.configId, playerNetId, hand, liveRoom);
        }
    }

    // ── Container handlers ────────────────────────────────────────────────────

    // Garde l'enum pour le typage de IPlaceContext.Kind (lecture côté code orchestrateur).
    // La classification placeId → PlaceKind et la résolution placeKey → UUID backend sont
    // désormais portées par ResolvePlace + les IPlaceContext concrets.
    private enum PlaceKind { Unknown, HandLeft, HandRight, Container, Pocket, ItemContainer, Back }

    // Back = « hand-like » : mono-slot, porté par une entité runtime attachée au joueur.
    private static bool IsHand(PlaceKind k) => k == PlaceKind.HandLeft || k == PlaceKind.HandRight || k == PlaceKind.Back;
    private static bool IsContainerKind(PlaceKind k) => k == PlaceKind.Container || k == PlaceKind.ItemContainer;

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
                int qty = Mathf.Max(1, it.quantity);
                SeedItemStateFromDb(it.configId, it.Id, it.stateData); // arme stockée : réhydrate son chargeur
                session.ItemsByEntityId[entityId] = new ContainerSessionItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex,
                    ItemUuid = it.Id, Version = it.version, Quantity = qty,
                };
                if (!session.SlotToEntityId.ContainsKey(slotIndex)) session.SlotToEntityId[slotIndex] = entityId;
                snapshot.Add(new S2C_ContainerItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex, Quantity = qty,
                    LoadedAmmo = SnapshotLoadedAmmo(it.configId, it.Id),
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
                && (hands.LeftEntityId == msg.EntityId || hands.RightEntityId == msg.EntityId
                    || hands.BackEntityId == msg.EntityId));
        var itemCfg = DatabaseManager.GetItemConfigById(entity.ItemConfigId);
        float openRange = (itemCfg != null ? itemCfg.RangeToInteract : 1.5f) + 1.5f;
        if (!held && Vector3.Distance(conn.identity.transform.position, entity.Position) > openRange) {
            conn.Send(new S2C_ItemContainerOpenFailed { EntityId = msg.EntityId, ErrorMessage = "Trop loin" });
            return;
        }
        var itemContainerCfg = ItemContainerConfig.Of(itemCfg);
        if (itemContainerCfg == null || !itemContainerCfg.IsContainer) {
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
            OpenItemContainerCoroutine(conn, netId, msg.EntityId, bridge.Uuid, itemContainerCfg));
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
                int qty = Mathf.Max(1, it.quantity);
                SeedItemStateFromDb(it.configId, it.Id, it.stateData); // arme stockée : réhydrate son chargeur
                session.ItemsByEntityId[entityId] = new ContainerSessionItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex,
                    ItemUuid = it.Id, Version = it.version, Quantity = qty,
                };
                if (!session.SlotToEntityId.ContainsKey(slotIndex)) session.SlotToEntityId[slotIndex] = entityId;
                snapshot.Add(new S2C_ContainerItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex, Quantity = qty,
                    LoadedAmmo = SnapshotLoadedAmmo(it.configId, it.Id),
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
                    PropConfigId = p.configId, PropPresetId = p.presetIndex, Quantity = 1,
                };
                if (!session.SlotToEntityId.ContainsKey(slotIndex)) session.SlotToEntityId[slotIndex] = entityId;
                snapshot.Add(new S2C_ContainerItem {
                    EntityId = entityId, ConfigId = 0, SlotIndex = slotIndex, Quantity = 1,
                    PropConfigId = p.configId, PropPresetId = p.presetIndex,
                    LoadedAmmo = -1,
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
                appId      = PhoneAppIds.Career,
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
                PropConfigId = propConfigId, PropPresetId = presetId, Quantity = 1,
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

        string uuid = null; int configId = 0, version = 1, slotIndex = -1, srcQty = 1;
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
            srcQty = Mathf.Max(1, ci.Quantity);
            fromContainer = true;
        }
        else if (_pocketByPlayer.TryGetValue(netId, out pocketSession)
            && pocketSession.ItemsByEntityId.TryGetValue(msg.EntityId, out var pi)) {
            uuid = pi.ItemUuid; configId = pi.ConfigId; version = pi.Version; slotIndex = pi.SlotIndex;
            srcQty = 1; // invariant poche
            fromPocket = true;
            // Item équipé (projection hotbar) : déséquipe d'abord — la main ne doit
            // plus pointer sur un slot qui va se vider.
            OnPocketEntryRemoved(netId, msg.EntityId);
        }
        else return; // introuvable

        if (string.IsNullOrEmpty(uuid)) return;

        // Position de drop : devant le joueur, raycast au sol (layer 9).
        Vector3 dropPos = conn.identity.transform.position + conn.identity.transform.forward * 0.6f;
        if (Physics.Raycast(dropPos + Vector3.up, Vector3.down, out var hitRay, 5f, 1 << 9)) dropPos = hitRay.point;

        // Item-monde runtime au sol.
        int worldEntity = SpawnItem(roomId, configId, dropPos, Quaternion.identity);

        ItemConfig cfg = DatabaseManager.GetItemConfigById(configId);
        string worldPlaceId = ResolveWorldPlaceId(conn, roomId);
        bool toPersist = cfg != null && cfg.ToPersist && !string.IsNullOrEmpty(worldPlaceId);

        // Source pile (qty>1) : on lâche 1 unité — la pile reste en place et la ligne DB
        // garde son UUID. On crée une NOUVELLE ligne monde (POST qty=1) + PATCH source -=1.
        // Invariant world items = qty 1 préservé.
        if (srcQty > 1 && fromContainer) {
            ApiManager.Instance?.StartCoroutine(SplitDropFromContainerCoroutine(
                conn, contSession, msg.EntityId, configId, uuid, version, srcQty,
                worldEntity, worldPlaceId, dropPos, toPersist));
            GameLogger.Network.Info("DropFromInventory split-1 srcQty={Qty} uuid={Uuid} configId={Config} netId={NetId}",
                srcQty, uuid, configId, netId);
            return;
        }

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

        ApiManager.Instance?.StartCoroutine(
            DropFromInventoryCoroutine(worldEntity, uuid, version, worldPlaceId, dropPos, toPersist));

        GameLogger.Network.Info("DropFromInventory uuid={Uuid} configId={Config} toPersist={Persist} netId={NetId}",
            uuid, configId, toPersist, netId);
    }

    /// <summary>Split-1 du flow "lâcher au sol" : la pile source garde son UUID et perd 1 unité ;
    /// une nouvelle ligne monde qty=1 est créée (POST si persistant, sinon item-monde éphémère).
    /// Rollback symétrique en cas d'échec.</summary>
    private IEnumerator SplitDropFromContainerCoroutine(
        NetworkConnectionToClient conn, ItemContainerSession contSession,
        int srcEntityId, int configId, string srcUuid, int srcVersion, int srcQty,
        int worldEntity, string worldPlaceId, Vector3 dropPos, bool toPersist) {
        // 1. Si persistant : POST world item qty=1, sinon item-monde éphémère.
        string newWorldUuid = null; int newWorldVersion = 1;
        if (toPersist) {
            var createBody = new CreateItemBody {
                placeId  = worldPlaceId,
                configId = configId,
                quantity = 1,
                position = new Vector3Body(dropPos),
                rotation = new Vector3Body(Vector3.zero),
            };
            UnityWebRequest createReq = ApiManager.Instance.CreateItemRequest(createBody);
            yield return createReq.SendWebRequest();
            if (createReq.responseCode < 200 || createReq.responseCode >= 300) {
                Debug.LogWarning($"[ServerItemManager] SplitDrop POST world failed code={createReq.responseCode} — abort, despawn runtime");
                DespawnItem(PlayerRoomTracker.Instance.GetRoom(conn), worldEntity);
                yield break;
            }
            try { var it = JsonConvert.DeserializeObject<ItemJson>(createReq.downloadHandler.text); if (it != null) { newWorldUuid = it.Id; newWorldVersion = it.version; } } catch { }
        }

        // 2. PATCH source qty -=1.
        var patchBody = new UpdateItemBody {
            expectedVersion = srcVersion,
            quantity = srcQty - 1,
        };
        UnityWebRequest patchReq = ApiManager.Instance.UpdateItemRequest(srcUuid, patchBody);
        yield return patchReq.SendWebRequest();
        if (patchReq.responseCode < 200 || patchReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] SplitDrop PATCH src failed code={patchReq.responseCode} — rollback world row + despawn");
            if (!string.IsNullOrEmpty(newWorldUuid))
                yield return ApiManager.Instance.DeleteItemRequest(newWorldUuid).SendWebRequest();
            DespawnItem(PlayerRoomTracker.Instance.GetRoom(conn), worldEntity);
            yield break;
        }
        int srcNewVersion = srcVersion + 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(patchReq.downloadHandler.text); if (it != null) srcNewVersion = it.version; } catch { }

        // 3. Met à jour la session source (qty-=1, version OCC) et bridge la cible monde.
        if (contSession.ItemsByEntityId.TryGetValue(srcEntityId, out var si)) {
            si.Quantity = Mathf.Max(1, srcQty - 1);
            si.Version = srcNewVersion;
            if (_bridges.TryGetValue(srcEntityId, out var br)) br.Version = srcNewVersion;
        }
        if (toPersist && !string.IsNullOrEmpty(newWorldUuid))
            AssociateUuid(worldEntity, newWorldUuid, newWorldVersion, worldPlaceId);

        SendItemContainerSnapshot(conn, contSession);
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

        // Sac de butin vidé pendant cette session → disparition immédiate (sinon il attend
        // son expiration 10 min). No-op pour un conteneur normal (prop/coffre).
        if (session.ItemsByEntityId.Count == 0) TryDespawnEmptyLootBag(session.PlaceId);

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

        // ── Fusion de liasses (cash) : déposer une liasse sur une autre ADDITIONNE les
        // montants — jamais de pile, une seule liasse subsiste (la cible). Résolu AVANT la
        // détection de merge de pile : le cash est maxStackSize=1 et serait refusé en aval.
        if (itemCtx.ConfigId == CashItem.ConfigId) {
            int cashTgtEntityId = -1; string cashTgtUuid = null;
            bool cashTarget = false;
            if (toCtx is PocketPlaceContext ppcCash)
                cashTarget = ppcCash.TryResolveSameConfig(msg.ToSlotIndex, itemCtx.ConfigId, out cashTgtEntityId, out cashTgtUuid);
            else if (toCtx is ContainerPlaceContext cpcCash)
                cashTarget = cpcCash.TryResolveStackMerge(msg.ToSlotIndex, itemCtx.ConfigId,
                    out cashTgtEntityId, out cashTgtUuid, out _, out _, out _);
            else if (toCtx is ItemContainerPlaceContext icpcCash)
                cashTarget = icpcCash.TryResolveStackMerge(msg.ToSlotIndex, itemCtx.ConfigId,
                    out cashTgtEntityId, out cashTgtUuid, out _, out _, out _);
            if (cashTarget && cashTgtEntityId != msg.EntityId
                && !string.IsNullOrEmpty(itemCtx.ItemUuid) && !string.IsNullOrEmpty(cashTgtUuid)) {
                ApiManager.Instance?.StartCoroutine(MergeCashIntoTargetCoroutine(conn, msg, itemCtx,
                    fromCtx, toCtx, cashTgtEntityId, cashTgtUuid));
                return;
            }
        }

        // ── Détection d'un merge de pile sur la cible ─────────────────────────
        // Si le slot cible contient une pile de MÊME config avec maxStackSize>1, on bypass
        // IsSlotAvailableFor (l'occupation est légitime) puis on dispatch MergeIntoTarget.
        // Si la pile est pleine → refus "Slot plein".
        bool mergeable = false;
        int mergeTargetEntityId = -1, mergeTargetVersion = 0, mergeTargetQty = 0, mergeMaxStack = 1;
        string mergeTargetUuid = null;
        if (toCtx is ContainerPlaceContext cpc) {
            mergeable = cpc.TryResolveStackMerge(msg.ToSlotIndex, itemCtx.ConfigId,
                out mergeTargetEntityId, out mergeTargetUuid, out mergeTargetVersion, out mergeTargetQty, out mergeMaxStack);
        } else if (toCtx is ItemContainerPlaceContext icpc) {
            mergeable = icpc.TryResolveStackMerge(msg.ToSlotIndex, itemCtx.ConfigId,
                out mergeTargetEntityId, out mergeTargetUuid, out mergeTargetVersion, out mergeTargetQty, out mergeMaxStack);
        }
        // Un item ne se merge pas avec lui-même (déposer le draggable sur son propre slot).
        if (mergeable && mergeTargetEntityId == msg.EntityId) mergeable = false;

        if (!mergeable && !toCtx.IsSlotAvailableFor(msg.ToSlotIndex, msg.EntityId)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Slot occupé" });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            return;
        }
        if (mergeable && mergeTargetQty >= mergeMaxStack) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = InventoryToasts.SlotFull });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            return;
        }

        // Dispatch stratégie :
        //   - mergeable → MergeIntoTargetCoroutine (PATCH target += k ; PATCH source -=k OU DELETE+TearDown)
        //   - source pile (qty>1) + cible Hand/Pocket → SplitOneToTargetCoroutine (POST qty=1 ; PATCH source -=1)
        //   - sinon → MoveItemCoroutine (PlainMove, préserve quantity)
        if (mergeable) {
            ApiManager.Instance?.StartCoroutine(MergeIntoTargetCoroutine(conn, msg, itemCtx, fromCtx, toCtx,
                mergeTargetEntityId, mergeTargetUuid, mergeTargetVersion, mergeTargetQty, mergeMaxStack));
            return;
        }
        bool toIsHandOrPocket = IsHand(toCtx.Kind) || toCtx.Kind == PlaceKind.Pocket;
        if (itemCtx.Quantity > 1 && toIsHandOrPocket) {
            ApiManager.Instance?.StartCoroutine(SplitOneToTargetCoroutine(conn, msg, itemCtx, fromCtx, toCtx, charId));
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
                stateData       = MoveStateData(itemCtx.ConfigId, itemCtx.ItemUuid, toCtx.HasSlotIndex, msg.ToSlotIndex),
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

    // ── Split 1 : extraction d'une unité d'une pile vers une main / poche ────
    // Source = container/itemContainer avec qty>1 ; cible = Hand ou Pocket.
    // Crée une nouvelle ligne DB qty=1 à la cible (nouveau UUID), puis PATCH la source
    // pour décrémenter sa quantité. Rollback : DELETE le nouveau row si PATCH source rate.
    // Aucune modification du slot/uuid de la source — la pile reste ancrée.
    private IEnumerator SplitOneToTargetCoroutine(NetworkConnectionToClient conn, C2S_MoveItem msg,
        ItemCtx itemCtx, IPlaceContext fromCtx, IPlaceContext toCtx, string charId)
    {
        if (string.IsNullOrEmpty(itemCtx.ItemUuid)) {
            // Pile éphémère = inattendu (les piles vivent en DB). Refus défensif.
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Pile non persistée" });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            yield break;
        }

        // 1. POST /items qty=1 sur la cible.
        var createBody = new CreateItemBody {
            placeId  = toCtx.PlaceId,
            configId = itemCtx.ConfigId,
            quantity = 1,
            stateData = toCtx.HasSlotIndex
                ? new Dictionary<string, object> { { "slotIndex", msg.ToSlotIndex } }
                : null,
            ownedBy = charId,
        };
        UnityWebRequest createReq = ApiManager.Instance.CreateItemRequest(createBody);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode < 200 || createReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] SplitOne POST failed code={createReq.responseCode}");
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = $"Échec split ({createReq.responseCode})" });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            yield break;
        }
        string newUuid = null; int newVersion = 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(createReq.downloadHandler.text); if (it != null) { newUuid = it.Id; newVersion = it.version; } }
        catch { }
        if (string.IsNullOrEmpty(newUuid)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Split sans uuid" });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            yield break;
        }

        // 2. PATCH source qty -=1 (place inchangée).
        var patchBody = new UpdateItemBody {
            expectedVersion = itemCtx.Version,
            quantity = itemCtx.Quantity - 1,
        };
        UnityWebRequest patchReq = ApiManager.Instance.UpdateItemRequest(itemCtx.ItemUuid, patchBody);
        yield return patchReq.SendWebRequest();
        if (patchReq.responseCode < 200 || patchReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] SplitOne PATCH source failed code={patchReq.responseCode} — rollback");
            yield return ApiManager.Instance.DeleteItemRequest(newUuid).SendWebRequest();
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = $"Échec split src ({patchReq.responseCode})" });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            yield break;
        }
        int srcNewVersion = itemCtx.Version + 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(patchReq.downloadHandler.text); if (it != null) srcNewVersion = it.version; } catch { }

        // 3. Décrémente la quantité de la pile source EN PLACE (pas de tear-down) +
        //    alloue un entityId neuf pour la cible et Install.
        if (fromCtx is ContainerPlaceContext fromCpc)
            fromCpc.UpdateQuantity(msg.EntityId, itemCtx.Quantity - 1, srcNewVersion);
        else if (fromCtx is ItemContainerPlaceContext fromIcpc)
            fromIcpc.UpdateQuantity(msg.EntityId, itemCtx.Quantity - 1, srcNewVersion);

        int newEntityId = _nextEntityId++;
        var newCtx = new ItemCtx {
            ConfigId = itemCtx.ConfigId, ItemUuid = newUuid, Version = newVersion, Quantity = 1,
        };
        toCtx.Install(newEntityId, newCtx, msg.ToSlotIndex, newVersion);

        conn.Send(new S2C_MoveItemResult { Success = true, EntityId = msg.EntityId });
        PushSnapshotsFor(conn, fromCtx, toCtx);
        PropagateContainerUpdateToOtherSubscribers(conn.identity.netId, fromCtx, toCtx);
    }

    // ── Merge dans une pile cible ────────────────────────────────────────────
    // Source = main, poche, container ou itemContainer. Cible = pile (qty<max) de même config.
    // Container→Container : transfère k = min(srcQty, max-tgtQty). Si source devient 0 → DELETE+TearDown.
    // Hand/Pocket→Container : k=1. DELETE source si UUID (persistée) sinon juste TearDown (éphémère).
    private IEnumerator MergeIntoTargetCoroutine(NetworkConnectionToClient conn, C2S_MoveItem msg,
        ItemCtx itemCtx, IPlaceContext fromCtx, IPlaceContext toCtx,
        int tgtEntityId, string tgtUuid, int tgtVersion, int tgtQty, int maxStack)
    {
        bool srcIsContainer = IsContainerKind(fromCtx.Kind);
        int srcQty = srcIsContainer ? Mathf.Max(1, itemCtx.Quantity) : 1;
        int k = Mathf.Min(srcQty, maxStack - tgtQty);
        if (k <= 0) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = InventoryToasts.SlotFull });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            yield break;
        }

        // 1. PATCH target qty += k.
        var tgtBody = new UpdateItemBody {
            expectedVersion = tgtVersion,
            quantity = tgtQty + k,
        };
        UnityWebRequest tgtReq = ApiManager.Instance.UpdateItemRequest(tgtUuid, tgtBody);
        yield return tgtReq.SendWebRequest();
        if (tgtReq.responseCode < 200 || tgtReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] Merge PATCH target failed code={tgtReq.responseCode}");
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = $"Échec merge ({tgtReq.responseCode})" });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            yield break;
        }
        int tgtNewVersion = tgtVersion + 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(tgtReq.downloadHandler.text); if (it != null) tgtNewVersion = it.version; } catch { }

        // 2. Source : PATCH qty -=k OU DELETE si épuisé.
        bool srcPersisted = !string.IsNullOrEmpty(itemCtx.ItemUuid);
        bool srcExhausted = srcQty - k <= 0;
        if (srcPersisted) {
            if (srcExhausted) {
                UnityWebRequest delReq = ApiManager.Instance.DeleteItemRequest(itemCtx.ItemUuid);
                yield return delReq.SendWebRequest();
                if (delReq.responseCode < 200 || delReq.responseCode >= 300) {
                    Debug.LogWarning($"[ServerItemManager] Merge DELETE src failed code={delReq.responseCode} — rollback target");
                    yield return RollbackTargetQuantity(tgtUuid, tgtNewVersion, tgtQty);
                    conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Échec merge src" });
                    PushSnapshotsFor(conn, fromCtx, toCtx);
                    yield break;
                }
            } else {
                var srcBody = new UpdateItemBody {
                    expectedVersion = itemCtx.Version,
                    quantity = srcQty - k,
                };
                UnityWebRequest srcReq = ApiManager.Instance.UpdateItemRequest(itemCtx.ItemUuid, srcBody);
                yield return srcReq.SendWebRequest();
                if (srcReq.responseCode < 200 || srcReq.responseCode >= 300) {
                    Debug.LogWarning($"[ServerItemManager] Merge PATCH src failed code={srcReq.responseCode} — rollback target");
                    yield return RollbackTargetQuantity(tgtUuid, tgtNewVersion, tgtQty);
                    conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = $"Échec merge src ({srcReq.responseCode})" });
                    PushSnapshotsFor(conn, fromCtx, toCtx);
                    yield break;
                }
                int srcNewVersion = itemCtx.Version + 1;
                try { var it = JsonConvert.DeserializeObject<ItemJson>(srcReq.downloadHandler.text); if (it != null) srcNewVersion = it.version; } catch { }
                // Met à jour la session source in-place (pile partiellement vidée).
                if (fromCtx is ContainerPlaceContext fromCpc)
                    fromCpc.UpdateQuantity(msg.EntityId, srcQty - k, srcNewVersion);
                else if (fromCtx is ItemContainerPlaceContext fromIcpc)
                    fromIcpc.UpdateQuantity(msg.EntityId, srcQty - k, srcNewVersion);
            }
        }
        // 3. Si la source est épuisée (ou main éphémère), tear-down de la source.
        if (srcExhausted) fromCtx.TearDown(msg.EntityId);

        // 4. Met à jour la quantité de la pile cible (session in-place).
        if (toCtx is ContainerPlaceContext toCpc)
            toCpc.UpdateQuantity(tgtEntityId, tgtQty + k, tgtNewVersion);
        else if (toCtx is ItemContainerPlaceContext toIcpc)
            toIcpc.UpdateQuantity(tgtEntityId, tgtQty + k, tgtNewVersion);

        conn.Send(new S2C_MoveItemResult { Success = true, EntityId = msg.EntityId });
        PushSnapshotsFor(conn, fromCtx, toCtx);
        PropagateContainerUpdateToOtherSubscribers(conn.identity.netId, fromCtx, toCtx);
    }

    // ── Fusion de liasses (cash) ─────────────────────────────────────────────
    // Déposer une liasse sur une autre ne fait pas de pile : les MONTANTS s'additionnent
    // dans la liasse CIBLE et la source est supprimée. DELETE de la source d'abord : en
    // cas d'échec backend rien n'a bougé, et le montant cible n'est crédité qu'une fois
    // la source réellement détruite — pas de duplication d'argent possible.
    private IEnumerator MergeCashIntoTargetCoroutine(NetworkConnectionToClient conn, C2S_MoveItem msg,
        ItemCtx itemCtx, IPlaceContext fromCtx, IPlaceContext toCtx, int tgtEntityId, string tgtUuid)
    {
        int srcAmount = GetCashAmount(msg.EntityId);
        int tgtAmount = _cashAmountByKey.TryGetValue(tgtUuid, out int ta) ? ta : 0;

        UnityWebRequest delReq = ApiManager.Instance.DeleteItemRequest(itemCtx.ItemUuid);
        yield return delReq.SendWebRequest();
        if (delReq.responseCode < 200 || delReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] MergeCash DELETE src failed code={delReq.responseCode} uuid={itemCtx.ItemUuid}");
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Échec de la fusion" });
            PushSnapshotsFor(conn, fromCtx, toCtx);
            yield break;
        }

        _cashAmountByKey.Remove(itemCtx.ItemUuid);
        _cashAmountByKey.Remove("e:" + msg.EntityId);
        fromCtx.TearDown(msg.EntityId); // main : despawn monde ; poche : déséquipe + purge session
        _cashAmountByKey[tgtUuid] = tgtAmount + srcAmount;
        QueueCashPersist(tgtEntityId);

        conn.Send(new S2C_MoveItemResult { Success = true, EntityId = msg.EntityId });
        PushSnapshotsFor(conn, fromCtx, toCtx);
        PropagateContainerUpdateToOtherSubscribers(conn.identity.netId, fromCtx, toCtx);
        GameLogger.Network.Info("CashMerge netId={NetId} srcUuid={Src} tgtUuid={Tgt} total={Total}",
            conn.identity.netId, itemCtx.ItemUuid, tgtUuid, tgtAmount + srcAmount);
    }

    private IEnumerator RollbackTargetQuantity(string tgtUuid, int tgtCurrentVersion, int tgtOriginalQty) {
        var body = new UpdateItemBody {
            expectedVersion = tgtCurrentVersion,
            quantity = tgtOriginalQty,
        };
        UnityWebRequest req = ApiManager.Instance.UpdateItemRequest(tgtUuid, body);
        yield return req.SendWebRequest();
        if (req.responseCode < 200 || req.responseCode >= 300)
            Debug.LogError($"[ServerItemManager] RollbackTargetQuantity FAILED code={req.responseCode} uuid={tgtUuid} — DB désynchronisée jusqu'au prochain snapshot");
    }

    // ── Swap items (atomic two-PATCH) ────────────────────────────────────────
    // Pour hand↔container, hand↔pocket et container↔container (même placeId).

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
        bool aEphemeralBadTarget = string.IsNullOrEmpty(itemA.ItemUuid) && !IsHand(ctxB.Kind);
        bool bEphemeralBadTarget = string.IsNullOrEmpty(itemB.ItemUuid) && !IsHand(ctxA.Kind);
        if (aEphemeralBadTarget || bEphemeralBadTarget) {
            int rejectedEntity = aEphemeralBadTarget ? msg.EntityIdA : msg.EntityIdB;
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = rejectedEntity,
                ErrorMessage = InventoryToasts.NotStorable });
            PushSnapshotsFor(conn, ctxA, ctxB);
            return;
        }

        // Garde stack ↔ main : un swap impliquant une main et une pile (qty>1) est refusé
        // (la main ne peut tenir qu'1 unité ; le split-1 est dispo via drag vers main libre).
        // Idem pour main ↔ poche si jamais une pile y atterrissait par drift.
        bool aIsHand = IsHand(ctxA.Kind), bIsHand = IsHand(ctxB.Kind);
        bool aIsStack = itemA.Quantity > 1, bIsStack = itemB.Quantity > 1;
        if ((aIsHand && bIsStack) || (bIsHand && aIsStack)) {
            int rejectedEntity = aIsHand ? msg.EntityIdA : msg.EntityIdB;
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = rejectedEntity,
                ErrorMessage = InventoryToasts.StackPickOneAtATime });
            PushSnapshotsFor(conn, ctxA, ctxB);
            return;
        }

        ApiManager.Instance?.StartCoroutine(SwapItemsCoroutine(conn, msg, ctxA, itemA, ctxB, itemB));
    }

    // ── Split d'une pile dans un autre slot du MÊME conteneur ─────────────────
    // Source = entité dans le conteneur ouvert chez le joueur (prop OU item-container).
    // Cible = slot LIBRE dans le même conteneur. Crée une nouvelle ligne DB qty=K + PATCH
    // source qty-=K. Rollback symétrique. Le client valide qu'un slot libre existe avant
    // d'envoyer le message ; le serveur revalide (course possible avec un autre move).
    public void HandleSplitItem(NetworkConnectionToClient conn, C2S_SplitItem msg) {
        if (conn?.identity == null) return;
        uint netId = conn.identity.netId;
        var player = conn.identity.GetComponent<Sim.PlayerController>();
        string charId = player?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Sans caractère" });
            return;
        }

        // Localise la session conteneur courante (prop OU item-container) qui contient l'entité source.
        ContainerSession propSess = null;
        ItemContainerSession itemSess = null;
        ContainerSessionItem srcSI = null;
        if (_openContainerByPlayer.TryGetValue(netId, out var ps) && ps.ItemsByEntityId.TryGetValue(msg.EntityId, out var ci1)) {
            propSess = ps; srcSI = ci1;
        } else if (_openItemContainerByPlayer.TryGetValue(netId, out var ips) && ips.ItemsByEntityId.TryGetValue(msg.EntityId, out var ci2)) {
            itemSess = ips; srcSI = ci2;
        }
        if (srcSI == null || srcSI.IsProp) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Item introuvable" });
            return;
        }

        int srcQty = Mathf.Max(1, srcSI.Quantity);
        int k = Mathf.Clamp(msg.Quantity, 1, srcQty - 1);
        if (k <= 0) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Quantité invalide" });
            return;
        }

        string placeId = propSess != null ? propSess.PlaceId : itemSess.PlaceId;
        var slotMap = propSess != null ? propSess.SlotToEntityId : itemSess.SlotToEntityId;
        int slotCount = propSess != null ? propSess.Config.SlotCount : itemSess.Config.SlotCount;
        int toSlot = msg.ToSlotIndex;
        if (toSlot < 0 || toSlot >= slotCount || slotMap.ContainsKey(toSlot)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = msg.EntityId, ErrorMessage = "Slot cible indisponible" });
            return;
        }

        ApiManager.Instance?.StartCoroutine(SplitItemCoroutine(conn, netId, msg.EntityId, srcSI, k, placeId, toSlot, charId));
    }

    private IEnumerator SplitItemCoroutine(NetworkConnectionToClient conn, uint netId, int srcEntityId,
        ContainerSessionItem srcSI, int k, string placeId, int toSlot, string charId)
    {
        int srcQty = Mathf.Max(1, srcSI.Quantity);
        int srcVersion = srcSI.Version;
        string srcUuid = srcSI.ItemUuid;
        int configId = srcSI.ConfigId;
        if (string.IsNullOrEmpty(srcUuid)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = srcEntityId, ErrorMessage = "Pile non persistée" });
            yield break;
        }

        // 1. POST nouvelle ligne qty=k au slot cible.
        var createBody = new CreateItemBody {
            placeId = placeId, configId = configId, quantity = k, ownedBy = charId,
            stateData = new Dictionary<string, object> { { "slotIndex", toSlot } },
        };
        UnityWebRequest createReq = ApiManager.Instance.CreateItemRequest(createBody);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode < 200 || createReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] Split POST failed code={createReq.responseCode}");
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = srcEntityId, ErrorMessage = $"Échec split ({createReq.responseCode})" });
            yield break;
        }
        string newUuid = null; int newVersion = 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(createReq.downloadHandler.text); if (it != null) { newUuid = it.Id; newVersion = it.version; } }
        catch { }
        if (string.IsNullOrEmpty(newUuid)) {
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = srcEntityId, ErrorMessage = "Split sans uuid" });
            yield break;
        }

        // 2. PATCH source qty -=k.
        var patchBody = new UpdateItemBody { expectedVersion = srcVersion, quantity = srcQty - k };
        UnityWebRequest patchReq = ApiManager.Instance.UpdateItemRequest(srcUuid, patchBody);
        yield return patchReq.SendWebRequest();
        if (patchReq.responseCode < 200 || patchReq.responseCode >= 300) {
            Debug.LogWarning($"[ServerItemManager] Split PATCH src failed code={patchReq.responseCode} — rollback");
            yield return ApiManager.Instance.DeleteItemRequest(newUuid).SendWebRequest();
            conn.Send(new S2C_MoveItemResult { Success = false, EntityId = srcEntityId, ErrorMessage = $"Échec split src ({patchReq.responseCode})" });
            yield break;
        }
        int srcNewVersion = srcVersion + 1;
        try { var it = JsonConvert.DeserializeObject<ItemJson>(patchReq.downloadHandler.text); if (it != null) srcNewVersion = it.version; } catch { }

        // 3. Met à jour la session in-place + alloue entityId pour la nouvelle pile.
        srcSI.Quantity = srcQty - k;
        srcSI.Version = srcNewVersion;
        if (_bridges.TryGetValue(srcEntityId, out var srcBridge)) srcBridge.Version = srcNewVersion;

        int newEntityId = _nextEntityId++;
        var newSI = new ContainerSessionItem {
            EntityId = newEntityId, ConfigId = configId, SlotIndex = toSlot,
            ItemUuid = newUuid, Version = newVersion, Quantity = k,
        };
        if (_openContainerByPlayer.TryGetValue(netId, out var ps) && ps.PlaceId == placeId) {
            ps.ItemsByEntityId[newEntityId] = newSI;
            ps.SlotToEntityId[toSlot] = newEntityId;
            SendContainerSnapshot(conn, ps);
            PropagateContainerUpdateToOtherSubscribers(netId, new ContainerPlaceContext(this, ps), new ContainerPlaceContext(this, ps));
        } else if (_openItemContainerByPlayer.TryGetValue(netId, out var ips) && ips.PlaceId == placeId) {
            ips.ItemsByEntityId[newEntityId] = newSI;
            ips.SlotToEntityId[toSlot] = newEntityId;
            SendItemContainerSnapshot(conn, ips);
        }
        _bridges[newEntityId] = new ItemDbBridge { Uuid = newUuid, Version = newVersion, PlaceId = placeId };

        conn.Send(new S2C_MoveItemResult { Success = true, EntityId = srcEntityId });
        GameLogger.Network.Info("SplitItem netId={NetId} src={SrcEid} (qty {SrcOld}→{SrcNew}) → slot={ToSlot} qty={K} newUuid={NewUuid}",
            netId, srcEntityId, srcQty, srcQty - k, toSlot, k, newUuid);
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
            stateData       = MoveStateData(itemA.ConfigId, itemA.ItemUuid, ctxB.HasSlotIndex, msg.SlotIndexB),
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
            stateData       = MoveStateData(itemB.ConfigId, itemB.ItemUuid, ctxA.HasSlotIndex, msg.SlotIndexA),
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
                stateData       = MoveStateData(itemA.ConfigId, itemA.ItemUuid, ctxA.HasSlotIndex, msg.SlotIndexA),
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
                Quantity = Mathf.Max(1, it.Quantity),
                LoadedAmmo = it.IsProp ? -1 : SnapshotLoadedAmmo(it.ConfigId, it.ItemUuid),
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
                Quantity = Mathf.Max(1, it.Quantity),
                PropConfigId = it.IsProp ? it.PropConfigId : 0,
                PropPresetId = it.IsProp ? it.PropPresetId : 0,
                LoadedAmmo = it.IsProp ? -1 : SnapshotLoadedAmmo(it.ConfigId, it.ItemUuid),
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
                // Invariant : la poche ne stocke jamais de stack. Clamp défensif + log si drift.
                int dbQty = Mathf.Max(1, it.quantity);
                if (dbQty > 1)
                    Debug.LogWarning($"[ServerItemManager] Pocket item id={it.Id} cfg={it.configId} a quantity={dbQty}>1 en DB — clamp à 1 (invariant poche = 1/slot).");
                SeedItemStateFromDb(it.configId, it.Id, it.stateData); // arme en poche : réhydrate son chargeur
                session.ItemsByEntityId[entityId] = new PocketSessionItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex,
                    ItemUuid = it.Id, Version = it.version, Quantity = 1,
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
                Quantity = Mathf.Max(1, it.Quantity),
                LoadedAmmo = SnapshotLoadedAmmo(it.ConfigId, it.ItemUuid),
                CashAmount = SnapshotCashAmount(it.ConfigId, it.ItemUuid),
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
        _handProjection.Remove(netId);
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
        public int    Quantity;    // ≥1 ; pour Hand toujours 1 (invariant). Sert au dispatch stack (split-1, merge, refus swap).
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

        public PlaceKind Kind         => _hand == HandType.Left ? PlaceKind.HandLeft
                                       : _hand == HandType.Back ? PlaceKind.Back
                                       : PlaceKind.HandRight;
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
            // Invariant : un item tenu en main = toujours 1 unité (pas de stack en main).
            ctx = new ItemCtx { ConfigId = entity.ItemConfigId, ItemUuid = uuid, Version = version, Quantity = 1 };
            return true;
        }

        public bool ValidateAsTarget(int slotIndex, int configId, out string err) {
            err = null;
            // Dos : seul un conteneur « wearable » (sac à dos) se porte. Les règles
            // mains (munitions, deux-mains) ne s'appliquent pas à ce slot.
            if (_hand == HandType.Back) {
                var backCfg = DatabaseManager.GetItemConfigById(configId);
                if (ItemContainerConfig.Of(backCfg)?.Wearable != true) {
                    err = InventoryToasts.NotWearable;
                    return false;
                }
                return true;
            }
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
            // Défense en profondeur : aucune main ne reçoit jamais une pile. Le dispatch
            // de MoveItemCoroutine (SplitOneToTarget) construit un ItemCtx.Quantity=1 explicite.
            if (ctx.Quantity != 1) {
                Debug.LogWarning($"[ServerItemManager] HandPlaceContext.Install reçu Quantity={ctx.Quantity} (attendu 1) — clamp à 1.");
            }
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
            ctx = new ItemCtx { ConfigId = si.ConfigId, ItemUuid = si.ItemUuid, Version = si.Version, Quantity = Mathf.Max(1, si.Quantity) };
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

        /// <summary>Renvoie true si le slot cible est occupé par une pile de même config avec
        /// place restante (qty &lt; maxStackSize). Utilisé par MoveItemCoroutine pour distinguer
        /// MergeIntoTarget d'un swap classique.</summary>
        public bool TryResolveStackMerge(int slotIndex, int incomingConfigId,
            out int targetEntityId, out string targetUuid, out int targetVersion,
            out int targetQty, out int maxStackSize) {
            targetEntityId = -1; targetUuid = null; targetVersion = 0; targetQty = 0; maxStackSize = 1;
            if (!_session.SlotToEntityId.TryGetValue(slotIndex, out int occ)) return false;
            if (!_session.ItemsByEntityId.TryGetValue(occ, out var si)) return false;
            if (si.IsProp || si.ConfigId != incomingConfigId) return false;
            var cfg = DatabaseManager.GetItemConfigById(si.ConfigId);
            int max = cfg != null ? cfg.MaxStackSize : 1;
            if (max <= 1) return false;
            if (si.Quantity >= max) { // pile pleine — merge refusé, mais le caller distingue ce cas
                targetEntityId = occ; targetUuid = si.ItemUuid; targetVersion = si.Version;
                targetQty = si.Quantity; maxStackSize = max; return true;
            }
            targetEntityId = occ; targetUuid = si.ItemUuid; targetVersion = si.Version;
            targetQty = si.Quantity; maxStackSize = max; return true;
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
                ItemUuid = ctx.ItemUuid, Version = newVersion, Quantity = Mathf.Max(1, ctx.Quantity),
            };
            _session.SlotToEntityId[slotIndex] = entityId;
            _mgr._bridges[entityId] = new ItemDbBridge { Uuid = ctx.ItemUuid, Version = newVersion, PlaceId = _session.PlaceId };
        }

        /// <summary>Met à jour la quantité (et version OCC) d'une pile existante en place sans
        /// déloger l'entityId. Utilisé après PATCH split/merge serveur-side.</summary>
        public void UpdateQuantity(int entityId, int newQuantity, int newVersion) {
            if (!_session.ItemsByEntityId.TryGetValue(entityId, out var si)) return;
            si.Quantity = Mathf.Max(1, newQuantity);
            si.Version = newVersion;
            if (_mgr._bridges.TryGetValue(entityId, out var br)) br.Version = newVersion;
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
            ctx = new ItemCtx { ConfigId = si.ConfigId, ItemUuid = si.ItemUuid, Version = si.Version, Quantity = Mathf.Max(1, si.Quantity) };
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

        /// <summary>Identique à <see cref="ContainerPlaceContext.TryResolveStackMerge"/>.</summary>
        public bool TryResolveStackMerge(int slotIndex, int incomingConfigId,
            out int targetEntityId, out string targetUuid, out int targetVersion,
            out int targetQty, out int maxStackSize) {
            targetEntityId = -1; targetUuid = null; targetVersion = 0; targetQty = 0; maxStackSize = 1;
            if (!_session.SlotToEntityId.TryGetValue(slotIndex, out int occ)) return false;
            if (!_session.ItemsByEntityId.TryGetValue(occ, out var si)) return false;
            if (si.IsProp || si.ConfigId != incomingConfigId) return false;
            var cfg = DatabaseManager.GetItemConfigById(si.ConfigId);
            int max = cfg != null ? cfg.MaxStackSize : 1;
            if (max <= 1) return false;
            targetEntityId = occ; targetUuid = si.ItemUuid; targetVersion = si.Version;
            targetQty = si.Quantity; maxStackSize = max; return true;
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
                ItemUuid = ctx.ItemUuid, Version = newVersion, Quantity = Mathf.Max(1, ctx.Quantity),
            };
            _session.SlotToEntityId[slotIndex] = entityId;
            _mgr._bridges[entityId] = new ItemDbBridge { Uuid = ctx.ItemUuid, Version = newVersion, PlaceId = _session.PlaceId };
        }

        /// <summary>Met à jour la quantité (et version OCC) d'une pile existante.</summary>
        public void UpdateQuantity(int entityId, int newQuantity, int newVersion) {
            if (!_session.ItemsByEntityId.TryGetValue(entityId, out var si)) return;
            si.Quantity = Mathf.Max(1, newQuantity);
            si.Version = newVersion;
            if (_mgr._bridges.TryGetValue(entityId, out var br)) br.Version = newVersion;
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
            // Invariant : la poche stocke toujours 1 item par slot — pas de stack.
            ctx = new ItemCtx { ConfigId = pi.ConfigId, ItemUuid = pi.ItemUuid, Version = pi.Version, Quantity = 1 };
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

        /// <summary>Occupant du slot s'il est de la MÊME config que l'item entrant (fusion de
        /// liasses). Équivalent poche du TryResolveStackMerge des conteneurs, sans notion de
        /// quantité (poche = 1 item/slot).</summary>
        public bool TryResolveSameConfig(int slotIndex, int incomingConfigId, out int entityId, out string uuid) {
            entityId = -1; uuid = null;
            if (!_mgr._pocketByPlayer.TryGetValue(_netId, out var s)) return false;
            if (!s.SlotToEntityId.TryGetValue(slotIndex, out int occ)) return false;
            if (!s.ItemsByEntityId.TryGetValue(occ, out var pi) || pi.ConfigId != incomingConfigId) return false;
            entityId = occ;
            uuid = pi.ItemUuid;
            return true;
        }

        public void TearDown(int entityId) {
            // L'item était peut-être projeté en main (hotbar) : déséquipe d'abord.
            _mgr.OnPocketEntryRemoved(_netId, entityId);
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
                ItemUuid = ctx.ItemUuid, Version = newVersion, Quantity = 1,
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
        if (placeId == $"backpack:{charId}") {
            string backend = inv?.BackpackPlaceId ?? placeId;
            return new HandPlaceContext(this, conn, netId, HandType.Back, roomId, backend);
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
                int qty = Mathf.Max(1, it.quantity);
                SeedItemStateFromDb(it.configId, it.Id, it.stateData); // arme stockée : réhydrate son chargeur
                session.ItemsByEntityId[entityId] = new ContainerSessionItem {
                    EntityId = entityId, ConfigId = it.configId, SlotIndex = slotIndex,
                    ItemUuid = it.Id, Version = it.version, Quantity = qty,
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
