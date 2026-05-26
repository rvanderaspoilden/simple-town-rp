using Mirror;
using Sim;
using Sim.Building;
using Sim.Entities.Persistence;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

/// <summary>
/// Route les messages C2S_PropInteraction vers le handler serveur approprié.
/// Les handlers synchrones s'exécutent directement.
/// Les handlers asynchrones (HTTP) délèguent à PropInteractionDispatcher.
///
/// Pour ajouter un nouveau type de prop interactif :
///   1. Ajouter un case dans Route()
///   2. Écrire un HandleXxx() ou déléguer au dispatcher
/// </summary>
public static class PropInteractionRouter {
    /// <summary>ItemConfig id of the debris item spawned when a prop is destroyed.</summary>
    private const int DebrisItemConfigId = 100;

    public static void Route(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        switch (msg.Type) {
            case PropType.Generic:      HandleGeneric     (conn, msg); break;
            case PropType.Seat:         HandleSeat        (conn, msg); break;
            case PropType.PaintBucket:  HandlePaintBucket (conn, msg); break;
            case PropType.Dispenser:    HandleDispenser   (conn, msg); break;
            case PropType.DeliveryBox:  HandleDeliveryBox (conn, msg); break;
            case PropType.Package:      HandlePackage     (conn, msg); break;
            case PropType.Door:         HandleDoor        (conn, msg); break;
            case PropType.Trash:        HandleTrash       (conn, msg); break;
            default:
                Debug.LogWarning($"[PropInteractionRouter] Unhandled PropType={msg.Type} from conn={conn.connectionId}");
                break;
        }
    }

    // ── Generic ───────────────────────────────────────────────────────────────

    private static void HandleGeneric(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var state)) {
            Debug.LogWarning($"[PropInteractionRouter] Generic prop {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        if (GenericPropInteraction.IsBuildRequest(msg.Payload)) {
            PropStateHeader header = PropStateHeader.ReadFrom(state.Payload);
            if (header.IsBuilt) return; // already built

            // Update isBuilt in the header and broadcast new state to the room
            header = new PropStateHeader { IsBuilt = true, PresetId = header.PresetId };
            byte[] updatedPayload = state.Payload.Length >= PropStateHeader.ByteSize
                ? (byte[])state.Payload.Clone()
                : new byte[PropStateHeader.ByteSize];
            header.WriteTo(updatedPayload, 0);

            ServerPropManager.Instance.UpdatePropState(msg.RoomId, msg.PropId, updatedPayload);

            // Phase 2 — relational sync. The runtime header just flipped to IsBuilt=true;
            // persist the same flag on the props row so the next load doesn't show the
            // prop as still-to-be-built.
            PropInteractionDispatcher.Instance?.SyncPropBuilt(msg.PropId, true);
        }
    }

    // ── Trash ─────────────────────────────────────────────────────────────────

    private static void HandleTrash(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (conn.identity == null) return;
        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out _)) {
            Debug.LogWarning($"[PropInteractionRouter] Trash prop {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (roomId == null) return;

        int entityId = TrashInteraction.GetEntityId(msg.Payload);
        ItemEntity entity = ServerItemManager.Instance.GetEntity(roomId, entityId);
        if (entity == null) return;
        if (entity.HolderNetId != conn.identity.netId) return; // doit tenir l'item
        if (entity.AuthorizedNetId != 0) return;               // item de mission : pas jetable

        // Retire l'item jeté (DespawnItem supprime aussi la ligne DB s'il était persisté).
        ServerItemManager.Instance.DespawnItem(roomId, entityId);

        // Diffuse le VFX eco à tous les clients de la room (le prop est en msg.RoomId).
        var thrown = new S2C_TrashThrown { PropId = msg.PropId, RoomId = msg.RoomId, ThrowerNetId = conn.identity.netId };
        foreach (var c in PlayerRoomTracker.Instance.GetConnectionsInRoom(msg.RoomId)) {
            if (c != null && c.isReady) c.Send(thrown);
        }

        Debug.Log($"[PropInteractionRouter] TrashThrow player={conn.identity.netId} entity={entityId} prop={msg.PropId} room='{roomId}'");
    }

    // ── Seat ──────────────────────────────────────────────────────────────────

    private static void HandleSeat(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        // Le joueur est un ICharacterEntity comme un autre — délégation à SeatService.
        // Le couch reste géré inline ici (pas de pendant NPC pour le moment).
        ICharacterEntity entity = conn.identity != null
            ? conn.identity.GetComponent<ICharacterEntity>()
            : null;
        if (entity == null) {
            Debug.LogWarning($"[PropInteractionRouter] Seat: connection {conn.connectionId} has no ICharacterEntity");
            return;
        }

        if (SeatInteraction.IsRevokeRequest(msg.Payload)) {
            // Couch : on ramène le PlayerState à IDLE (pas géré par SeatService).
            if (ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var s2)) {
                SeatState cur = SeatState.Deserialize(s2.Payload);
                if (ClearOccupant(cur.CouchOccupants, entity.OccupantId)) {
                    SetPlayerState(entity.OccupantId, PlayerState.IDLE);
                    ServerPropManager.Instance.UpdatePropState(msg.RoomId, msg.PropId, cur.Serialize());
                }
            }
            SeatService.ReleaseSeat(entity, msg.RoomId, msg.PropId);

        } else if (SeatInteraction.IsSitRequest(msg.Payload)) {
            SeatService.TryReserveSeat(entity, msg.RoomId, msg.PropId, out _);

        } else if (SeatInteraction.IsCouchRequest(msg.Payload)) {
            // Couch reste inline (pas d'API dédiée pour l'instant).
            if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var state)) return;
            SeatState current = SeatState.Deserialize(state.Payload);
            if (current.CouchOccupants == null) return;
            if (System.Array.Exists(current.CouchOccupants, id => id == entity.OccupantId)) return;
            int idx = System.Array.IndexOf(current.CouchOccupants, 0u);
            if (idx < 0) return;
            current.CouchOccupants[idx] = entity.OccupantId;
            SetPlayerState(entity.OccupantId, PlayerState.SLEEPING);
            ServerPropManager.Instance.UpdatePropState(msg.RoomId, msg.PropId, current.Serialize());
        }
    }

    /// <summary>
    /// Frees every seat/couch slot held by <paramref name="netId"/> in the given room.
    /// Called on disconnect.
    /// </summary>
    public static void ReleaseSeatsByPlayer(string roomId, uint netId) {
        foreach (ServerPropState state in ServerPropManager.Instance.GetRoomStates(roomId)) {
            if (state.Type != PropType.Seat) continue;
            SeatState current = SeatState.Deserialize(state.Payload);
            bool changed = ClearOccupant(current.SeatOccupants, netId)
                        | ClearOccupant(current.CouchOccupants, netId);
            if (changed)
                ServerPropManager.Instance.UpdatePropState(roomId, state.PropId, current.Serialize());
        }
    }

    // ── Seat helpers ──────────────────────────────────────────────────────────

    private static bool ClearOccupant(uint[] slots, uint netId) {
        if (slots == null) return false;
        bool changed = false;
        for (int i = 0; i < slots.Length; i++) {
            if (slots[i] == netId) { slots[i] = 0; changed = true; }
        }
        return changed;
    }

    private static void SetPlayerState(uint netId, PlayerState newState) {
        if (Mirror.NetworkServer.spawned.TryGetValue(netId, out var ni)) {
            PlayerController pc = ni.GetComponent<PlayerController>();
            if (pc != null) pc.PlayerState = newState;
        }
    }

    /// <summary>
    /// Resolves the apartment owned by the player on this connection (the tenant).
    /// Apartments share the hall room with the rest of the floor, so we cannot route
    /// by msg.RoomId — only the tenant can build/edit/remove inside their own apt.
    /// </summary>
    public static ApartmentController FindApartmentByConn(NetworkConnectionToClient conn) =>
        ServerApartmentRegistry.Instance.TryGetByConn(conn, out var apt) ? apt : null;

    // ── Build / Edit / Remove handlers (apartment build mode) ─────────────────

    public static void HandleBuildProp(NetworkConnectionToClient conn, C2S_BuildProp msg) {
        ApartmentController apt = FindApartmentByConn(conn);
        if (apt == null) {
            Debug.LogWarning($"[PropInteractionRouter] BuildProp denied: conn={conn.connectionId} owns no apartment");
            conn.Send(new S2C_BuildAck { Success = false });
            return;
        }
        PropInteractionDispatcher.Instance?.BuildProp(conn, msg, apt);
    }

    public static void HandleEditProp(NetworkConnectionToClient conn, C2S_EditProp msg) {
        ApartmentController apt = FindApartmentByConn(conn);
        if (apt == null) return;
        if (!apt.OwnsProp(msg.PropId)) {
            Debug.LogWarning($"[PropInteractionRouter] EditProp denied: prop {msg.PropId} not owned by tenant {apt.TenantId}");
            return;
        }

        ServerPropManager.Instance.UpdatePropTransform(apt.RoomId, msg.PropId, msg.Position, msg.Rotation);
        PropInteractionDispatcher.Instance?.SyncPropTransform(msg.PropId, msg.Position, msg.Rotation);
    }

    public static void HandleRemoveProp(NetworkConnectionToClient conn, C2S_RemoveProp msg) {
        ApartmentController apt = FindApartmentByConn(conn);
        if (apt == null) return;
        if (!apt.OwnsProp(msg.PropId)) {
            Debug.LogWarning($"[PropInteractionRouter] RemoveProp denied: prop {msg.PropId} not owned by tenant {apt.TenantId}");
            return;
        }

        // DELETE must happen BEFORE we wipe runtime state — the dispatcher reads
        // the UUID bridge from ServerPropManager, which RemoveProp clears.
        PropInteractionDispatcher.Instance?.SyncPropRemove(msg.PropId);
        ServerPropManager.Instance.RemoveProp(apt.RoomId, msg.PropId);
    }

    /// <summary>
    /// Destruction définitive d'un prop construit. Comme RemoveProp (DELETE en base +
    /// retrait runtime), mais laisse un item de débris à l'emplacement du prop.
    /// </summary>
    public static void HandleDestroyProp(NetworkConnectionToClient conn, C2S_DestroyProp msg) {
        ApartmentController apt = FindApartmentByConn(conn);
        if (apt == null) return;
        if (!apt.OwnsProp(msg.PropId)) {
            Debug.LogWarning($"[PropInteractionRouter] DestroyProp denied: prop {msg.PropId} not owned by tenant {apt.TenantId}");
            return;
        }

        // 1. Capture la position/rotation AVANT de retirer le prop du runtime.
        GameObject go  = ServerPropManager.Instance.GetSpawnedGameObject(msg.PropId);
        Vector3    pos = go != null ? go.transform.position : Vector3.zero;
        Quaternion rot = go != null ? go.transform.rotation : Quaternion.identity;

        // Prop mural : son origine est en hauteur sur le mur. Le débris doit tomber au
        // sol — on raycast vers le bas sur le layer sol (9) et on le pose à plat.
        if (go != null) {
            PropBehaviourBase beh = go.GetComponent<PropBehaviourBase>();
            if (beh != null && beh.IsWallProps()) {
                if (Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, out RaycastHit floorHit, 20f, 1 << 9))
                    pos = floorHit.point;
                rot = Quaternion.identity;
            }
        }

        // 2. DELETE en base (avant le wipe runtime : le dispatcher lit le bridge UUID).
        PropInteractionDispatcher.Instance?.SyncPropRemove(msg.PropId);

        // 3. Retrait runtime + broadcast S2C_DestroyProp aux clients de la pièce.
        ServerPropManager.Instance.RemoveProp(apt.RoomId, msg.PropId);

        // 4. Laisse un débris (item persistant) à la place. Persistant si l'appart a une
        //    place DB et que la config est ToPersist (survit au redémarrage → force le
        //    nettoyage) ; sinon éphémère en repli.
        if (go != null) {
            string placeId = apt.HomeData?.Id;
            if (!string.IsNullOrEmpty(placeId))
                ServerItemManager.Instance.SpawnPersistentWorldItem(apt.RoomId, placeId, DebrisItemConfigId, pos, rot, apt.TenantId);
            else
                ServerItemManager.Instance.SpawnItem(apt.RoomId, DebrisItemConfigId, pos, rot);
        }
    }

    // ── Sale: list / unlist / buy (player-to-player) ──────────────────────────

    public static void HandleSetForSale(NetworkConnectionToClient conn, C2S_SetPropForSale msg) {
        ApartmentController apt = FindApartmentByConn(conn);
        if (apt == null) {
            Debug.LogWarning($"[PropInteractionRouter] SetForSale denied: conn={conn.connectionId} owns no apartment");
            return;
        }
        if (!apt.OwnsProp(msg.PropId)) {
            Debug.LogWarning($"[PropInteractionRouter] SetForSale denied: prop {msg.PropId} not owned by tenant {apt.TenantId}");
            return;
        }

        // Guard: only furniture flagged sellable in its PropsConfig can be listed.
        if (ServerPropManager.Instance.TryGetPropState(apt.RoomId, msg.PropId, out var saleState)) {
            PropsConfig cfg = DatabaseManager.GetPropsById(saleState.PrefabId);
            if (cfg == null || !cfg.IsSellable()) {
                Debug.LogWarning($"[PropInteractionRouter] SetForSale denied: prop {msg.PropId} (config {saleState.PrefabId}) is not sellable");
                return;
            }
        }

        int price = Mathf.Max(0, msg.Price);
        ServerPropManager.Instance.SetSaleState(apt.RoomId, msg.PropId, true, price, apt.TenantId);
        PropInteractionDispatcher.Instance?.SyncPropSale(msg.PropId, true, price);
    }

    public static void HandleUnlist(NetworkConnectionToClient conn, C2S_UnlistProp msg) {
        ApartmentController apt = FindApartmentByConn(conn);
        if (apt == null) return;
        if (!apt.OwnsProp(msg.PropId)) {
            Debug.LogWarning($"[PropInteractionRouter] Unlist denied: prop {msg.PropId} not owned by tenant {apt.TenantId}");
            return;
        }

        ServerPropManager.Instance.SetSaleState(apt.RoomId, msg.PropId, false, 0, apt.TenantId);
        PropInteractionDispatcher.Instance?.SyncPropSale(msg.PropId, false, 0);
    }

    public static void HandleBuyProp(NetworkConnectionToClient conn, C2S_BuyProp msg) {
        // Magasin physique : un prop d'expo (ShopDisplay) crée une COPIE livrée à
        // l'acheteur et reste en place — c'est le flux du phone shop, pas le transfert
        // P2P. On branche ici avant de résoudre l'appartement vendeur (inexistant en City).
        if (ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var state) && state.IsShopDisplay) {
            PropInteractionDispatcher.Instance?.BuyShopDisplay(conn, msg);
            return;
        }

        // The whole flow is async (REST: transfer ownership, create delivery, record
        // transaction, optionally credit an offline seller) → delegate to the dispatcher.
        PropInteractionDispatcher.Instance?.BuyProp(conn, msg);
    }

    // ── PaintBucket ───────────────────────────────────────────────────────────
    // L'interaction OPEN est client-local (ouvre l'UI de peinture).
    // Le serveur ne reçoit une interaction que si le joueur applique une peinture.
    // Ici on laisse l'ApartmentController gérer l'application (code existant).

    private static void HandlePaintBucket(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out _)) {
            Debug.LogWarning($"[PropInteractionRouter] PaintBucket {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        // Ouverture de l'UI paint : aucun état serveur à modifier,
        // le comportement est entièrement client-local → le client ouvre l'UI directement.
        // Ce handler existe pour valider l'autorité si nécessaire à l'avenir.
        if (PaintBucketInteraction.IsOpenRequest(msg.Payload)) {
            Debug.Log($"[PropInteractionRouter] PaintBucket {msg.PropId} opened by conn={conn.connectionId}");
        }
    }

    // ── Dispenser ─────────────────────────────────────────────────────────────

    private static void HandleDispenser(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        try {
            if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var state)) {
                Debug.LogWarning($"[Dispenser] Purchase rejected: prop {msg.PropId} not found in room '{msg.RoomId}'");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                return;
            }

            int itemId = DispenserInteraction.GetItemId(msg.Payload);
            if (itemId < 0) {
                Debug.LogWarning($"[Dispenser] Purchase rejected: invalid payload from conn={conn.connectionId}");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                return;
            }

            Debug.Log($"[Dispenser] Purchase request player={conn.connectionId} prop={msg.PropId} item={itemId}");

            if (conn.identity == null) {
                Debug.LogWarning($"[Dispenser] Purchase rejected: conn={conn.connectionId} has no identity");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                return;
            }

            // Resolve DispenserConfiguration from registered PrefabId
            DispenserConfiguration dispenserConfig = null;
            if (state.PrefabId > 0) {
                PropsConfig propsConfig = DatabaseManager.GetPropsById(state.PrefabId);
                dispenserConfig = propsConfig as DispenserConfiguration;
            }

            if (dispenserConfig == null) {
                Debug.LogWarning($"[Dispenser] Purchase rejected: no DispenserConfiguration for prop {msg.PropId} (PrefabId={state.PrefabId}). Ensure PropsConfig.GetId() returns a non-zero value.");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                return;
            }

            // Find the item in the catalog (defensive: guard against null entries)
            ItemPrice? found = null;
            foreach (ItemPrice ip in dispenserConfig.ItemsToSell) {
                if (ip.item != null && ip.item.ID == itemId) { found = ip; break; }
            }

            if (found == null) {
                Debug.LogWarning($"[Dispenser] Purchase rejected: item {itemId} not found in catalog of dispenser {msg.PropId}");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                return;
            }

            ItemPrice itemPrice = found.Value;

            PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
            if (bank == null) {
                Debug.LogWarning($"[Dispenser] Purchase rejected: player conn={conn.connectionId} has no PlayerBankAccount");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                return;
            }

            if (bank.Money < itemPrice.price) {
                Debug.Log($"[Dispenser] Purchase rejected: insufficient funds player={conn.connectionId} has={bank.Money} needs={itemPrice.price}");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                return;
            }

            if (itemPrice.item.Prefab == null) {
                Debug.LogError($"[Dispenser] Purchase failed: ItemConfig {itemId} has no prefab assigned");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                return;
            }

            // Bloque l'achat tant que le joueur porte un item de mission (colis, etc.).
            if (ServerItemManager.Instance.IsHoldingMissionItem(conn.identity.netId)) {
                Debug.Log($"[Dispenser] Purchase rejected: mission item held player={conn.connectionId} item={itemId}");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                conn.Send(new ToastNotificationMessage {
                    text = "Termine ta mission avant d'acheter.",
                    typeByte = (byte)NotificationType.BANK,
                    worldToast = true
                });
                return;
            }

            // Vérifie l'espace en main AVANT de débiter le joueur. TWO_HAND exige
            // les deux mains libres ; ONE_HAND exige au moins une main libre.
            if (!ServerItemManager.Instance.CanFitInHand(conn.identity.netId, itemPrice.item)) {
                Debug.Log($"[Dispenser] Purchase rejected: hands full player={conn.connectionId} item={itemId}");
                conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
                conn.Send(new ToastNotificationMessage {
                    text = "Mains pleines : impossible d'acheter cet objet.",
                    typeByte = (byte)NotificationType.BANK,
                    worldToast = true
                });
                return;
            }

            Debug.Log($"[Dispenser] Purchase validated player={conn.connectionId} item={itemId} price={itemPrice.price}");

            bank.PostLedger(-itemPrice.price, LedgerReason.DispenserPurchase, LedgerCounterparty.System,
                LedgerCounterparty.Dispenser, configId: itemPrice.item.ID);

            string roomId = PlayerRoomTracker.Instance.GetRoom(conn) ?? "city";

            int entityId = ServerItemManager.Instance.SpawnItemInHand(roomId, itemPrice.item.ID, conn, itemPrice.item);
            Debug.Log($"[Dispenser] Assigning purchased item to hand entity={entityId} item={itemId}");

            conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = true, ItemId = itemId });
            Debug.Log($"[Dispenser] Purchase success item={itemId} entity={entityId}");
        }
        catch (System.Exception ex) {
            Debug.LogError($"[Dispenser] ERROR during purchase flow prop={msg.PropId}: {ex}");
            try { conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 }); }
            catch { /* connection may already be closing */ }
        }
    }

    // ── DeliveryBox ───────────────────────────────────────────────────────────
    // Délègue au dispatcher (MonoBehaviour) car la réponse nécessite un appel HTTP.

    private static void HandleDeliveryBox(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!DeliveryBoxInteraction.IsOpenRequest(msg.Payload)) return;

        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out _)) {
            Debug.LogWarning($"[PropInteractionRouter] DeliveryBox {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        PropInteractionDispatcher.Instance?.OpenDeliveryBox(conn, msg.PropId, msg.RoomId);
    }

    // ── Package ────────────────────────────────────────────────────────────────
    // L'ouverture d'un colis est client-local : elle déclenche le mode construction.
    // Le serveur valide juste l'existence du prop.

    private static void HandlePackage(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!PackageInteraction.IsOpenRequest(msg.Payload)) return;

        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out _)) {
            Debug.LogWarning($"[PropInteractionRouter] Package {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        Debug.Log($"[PropInteractionRouter] Package {msg.PropId} opened by conn={conn.connectionId}");
        // The actual opening logic (build mode) is handled client-side via PackageBehaviour.OnOpened event
    }

    // ── Door ───────────────────────────────────────────────────────────────────
    // LockRequest : owner only — toggles the front door lock state.
    // RingRequest : any non-owner — broadcasts S2C_DoorRing to all players in the room.

    private static void HandleDoor(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var state)) {
            Debug.LogWarning($"[PropInteractionRouter] Door {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        if (DoorInteraction.IsLockRequest(msg.Payload)) {
            // Validate ownership: the requesting player must be the tenant of the apartment
            // whose front door this prop represents.
            ApartmentController apt = ServerApartmentRegistry.Instance.FindOwnerOfProp(msg.PropId);
            if (apt == null) {
                Debug.LogWarning($"[PropInteractionRouter] Door lock denied: prop {msg.PropId} has no owning apartment");
                return;
            }

            Sim.PlayerController pc = conn.identity?.GetComponent<Sim.PlayerController>();
            if (pc == null || !apt.IsTenant(pc.CharacterData)) {
                Debug.LogWarning($"[PropInteractionRouter] Door lock denied: conn={conn.connectionId} is not tenant of apt={apt.ApartmentKey}");
                return;
            }

            DoorState current = DoorState.Deserialize(state.Payload);
            current.LockState = current.LockState == DoorLockState.LOCKED
                ? DoorLockState.UNLOCKED
                : DoorLockState.LOCKED;
            ServerPropManager.Instance.UpdatePropState(msg.RoomId, msg.PropId, current.Serialize());

            // Re-evaluate IsOpen from the door's trigger occupants. Sync() handles both transitions:
            //   - lock with occupants present → closes the door
            //   - unlock with occupants present → opens the door
            //   - either transition with no occupants → leaves the door closed
            DoorPropSource source = FindDoorSource(msg.PropId);
            if (source != null) source.Sync();

            // Persist the new lock state so it survives logout/server restart.
            // Dual-write: legacy scene_data + per-prop PATCH on the new model.
            PropInteractionDispatcher.Instance?.SyncPropState(msg.PropId, new System.Collections.Generic.Dictionary<string, object> {
                { "kind",       "door" },
                { "lockState",  (int)current.LockState },
                { "isOpen",     current.IsOpen },
                { "doorNumber", current.DoorNumber },
            });

            Debug.Log($"[PropInteractionRouter] Door {msg.PropId} lock={current.LockState} by conn={conn.connectionId}");

        } else if (DoorInteraction.IsRingRequest(msg.Payload)) {
            // Broadcast ring sound to all clients in the room
            var ring = new S2C_DoorRing { PropId = msg.PropId, RoomId = msg.RoomId };
            foreach (var c in PlayerRoomTracker.Instance.GetConnectionsInRoom(msg.RoomId)) {
                if (c != null && c.isReady) c.Send(ring);
            }
            Debug.Log($"[PropInteractionRouter] Door {msg.PropId} rung by conn={conn.connectionId} room={msg.RoomId}");
        }
    }

    /// <summary>
    /// Locates the DoorPropSource matching this propId. Front and inner doors may be either
    /// scene-registered or runtime-spawned depending on the apartment build flow, so we sweep
    /// all DoorPropSource instances instead of relying on ServerPropManager._spawnedGOs.
    /// </summary>
    private static DoorPropSource FindDoorSource(int propId) {
        foreach (var d in Object.FindObjectsByType<DoorPropSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            if (d.PropId == propId) return d;
        }
        return null;
    }

}
