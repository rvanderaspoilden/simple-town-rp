using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Newtonsoft.Json;
using Sim;
using Sim.Building;
using Sim.Entities;
using Sim.Entities.Persistence;
using Sim.Logging;
using Sim.Scriptables;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// MonoBehaviour singleton for prop interactions that require async HTTP calls.
/// Lives on the server; the router delegates here instead of blocking the message thread.
/// </summary>
public class PropInteractionDispatcher : MonoBehaviour {
    public static PropInteractionDispatcher Instance { get; private set; }

    private void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── DeliveryBox ───────────────────────────────────────────────────────────

    public void OpenDeliveryBox(NetworkConnectionToClient conn, int propId, string roomId) {
        PlayerController player = conn.identity?.GetComponent<PlayerController>();
        if (player == null) return;

        string characterId = player.CharacterData?.Id;
        if (string.IsNullOrEmpty(characterId)) return;

        StartCoroutine(FetchAndSendDeliveries(conn, propId, roomId, characterId));
    }

    private IEnumerator FetchAndSendDeliveries(
        NetworkConnectionToClient conn, int propId, string roomId, string characterId
    ) {
        UnityWebRequest req = ApiManager.Instance.RetrieveDeliveriesRequest(characterId);
        yield return req.SendWebRequest();

        Delivery[] deliveries;

        if (req.responseCode == 200) {
            DeliveryResponse response = JsonUtility.FromJson<DeliveryResponse>(req.downloadHandler.text);
            deliveries = response?.Deliveries != null
                ? System.Linq.Enumerable.ToArray(response.Deliveries)
                : System.Array.Empty<Delivery>();
        } else {
            Debug.LogWarning($"[PropInteractionDispatcher] DeliveryBox fetch failed ({req.responseCode}) for char {characterId}");
            deliveries = System.Array.Empty<Delivery>();
        }

        if (conn == null || !conn.isReady) yield break;

        conn.Send(new S2C_DeliveryBoxOpened {
            PropId     = propId,
            RoomId     = roomId,
            Deliveries = deliveries
        });
    }

    /// <summary>
    /// Server-side fetch of deliveries for a delivery box. Updates the prop state
    /// with the new delivery count (drives client visual). Used:
    ///   - on delivery box spawn (Init)
    ///   - after a build operation consumed a delivery
    /// </summary>
    public void RefreshDeliveryBoxCount(int propId, string roomId, string characterId) {
        StartCoroutine(FetchAndUpdateDeliveryCount(propId, roomId, characterId));
    }

    private IEnumerator FetchAndUpdateDeliveryCount(int propId, string roomId, string characterId) {
        UnityWebRequest req = ApiManager.Instance.RetrieveDeliveriesRequest(characterId);
        yield return req.SendWebRequest();

        uint count = 0;
        if (req.responseCode == 200) {
            DeliveryResponse response = JsonUtility.FromJson<DeliveryResponse>(req.downloadHandler.text);
            if (response?.Deliveries != null) count = (uint)response.Deliveries.Count;
        } else {
            Debug.LogWarning($"[PropInteractionDispatcher] DeliveryBox count fetch failed ({req.responseCode}) for char {characterId} prop={propId} room={roomId}");
        }

        if (!ServerPropManager.Instance.TryGetPropState(roomId, propId, out var state)) {
            Debug.LogWarning($"[PropInteractionDispatcher] DeliveryBox refresh: prop {propId} not found in room {roomId} (was the box despawned?)");
            yield break;
        }

        // Preserve header (built/preset) when updating the delivery count.
        PropStateHeader header = PropStateHeader.ReadFrom(state.Payload);
        byte[] newPayload = new DeliveryBoxState { Header = header, DeliveryCount = count }.Serialize();
        ServerPropManager.Instance.UpdatePropState(roomId, propId, newPayload);
        Debug.Log($"[PropInteractionDispatcher] DeliveryBox {propId} refreshed: count={count} (room={roomId})");
    }

    // ── Teleporter ────────────────────────────────────────────────────────────

    /// <summary>
    /// Routes an elevator use to the right BuildingBehavior based on the player's
    /// current roomId — no static registry, no event subscriptions. The roomId
    /// itself encodes the origin floor:
    ///   - "city"             → originFloor=0
    ///   - "hall:{street}:{n}" → originFloor=n, building looked up by street
    /// </summary>
    public void HandleTeleporterUse(NetworkConnectionToClient conn, int floorDestination) {
        string roomId = PlayerRoomTracker.Instance.GetRoom(conn);
        if (string.IsNullOrEmpty(roomId)) {
            GameLogger.Network.Warning("TeleporterUseNoRoom {ConnectionId}", conn.connectionId);
            return;
        }

        if (!TryResolveElevatorContext(roomId, out BuildingBehavior building, out int originFloor)) {
            GameLogger.Network.Warning("TeleporterUseUnresolvedRoom {ConnectionId} {RoomId}", conn.connectionId, roomId);
            return;
        }

        if (originFloor == floorDestination) return; // no-op (also guarded client-side)
        building.TeleportToFloor(originFloor, floorDestination, conn);
    }

    /// <summary>
    /// Maps a player's current roomId onto (building, originFloor). For "city"
    /// we pick the first BuildingBehavior in the scene — multi-building cities
    /// would need a building hint inside C2S_TeleporterUse.
    /// </summary>
    private static bool TryResolveElevatorContext(string roomId, out BuildingBehavior building, out int originFloor) {
        building = null;
        originFloor = 0;

        if (roomId == "city") {
            foreach (var bb in UnityEngine.Object.FindObjectsByType<BuildingBehavior>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                building = bb;
                return true;
            }
            return false;
        }

        if (roomId.StartsWith("hall:")) {
            string[] parts = roomId.Split(':');
            if (parts.Length >= 3
                && BuildingBehavior.TryGetBuilding(parts[1], out building)
                && int.TryParse(parts[2], out originFloor)) {
                return true;
            }
        }

        return false;
    }

    // ── Apartment covers ──────────────────────────────────────────────────────
    // Apartments share their room with the rest of the floor, so we resolve the
    // target apartment via the connection (only the tenant can paint covers).

    public void HandleApplyWallCovers(NetworkConnectionToClient conn, string roomId, byte[] coversJson) {
        if (!ServerApartmentRegistry.Instance.TryGetByConn(conn, out ApartmentController apt)) {
            Debug.LogWarning($"[PropInteractionDispatcher] ApplyWallCovers: conn {conn.connectionId} owns no apartment");
            return;
        }
        CoverData[] covers = CoverDataWrapper.Deserialize(coversJson);
        apt.ServerApplyWallCovers(covers);
        SyncCovers(apt.HomeData?.Id, BuildCoverEntries("wall", covers));
    }

    public void HandleApplyGroundCovers(NetworkConnectionToClient conn, string roomId, byte[] coversJson) {
        if (!ServerApartmentRegistry.Instance.TryGetByConn(conn, out ApartmentController apt)) {
            Debug.LogWarning($"[PropInteractionDispatcher] ApplyGroundCovers: conn {conn.connectionId} owns no apartment");
            return;
        }
        CoverData[] covers = CoverDataWrapper.Deserialize(coversJson);
        apt.ServerApplyGroundCovers(covers);
        SyncCovers(apt.HomeData?.Id, BuildCoverEntries("ground", covers));
    }

    private static IEnumerable<CoverApplyEntry> BuildCoverEntries(string surfaceKind, CoverData[] covers) {
        if (covers == null) yield break;
        foreach (CoverData cd in covers) {
            yield return new CoverApplyEntry {
                SurfaceKind   = surfaceKind,
                SurfaceIndex  = cd.idx,
                PaintConfigId = cd.paintConfigId,
                Color         = cd.additionalColor,
            };
        }
    }

    // ── Build prop (delivery → spawn → save) ──────────────────────────────────

    public void BuildProp(NetworkConnectionToClient conn, C2S_BuildProp msg, ApartmentController apt) {
        StartCoroutine(BuildPropCoroutine(conn, msg, apt));
    }

    private IEnumerator BuildPropCoroutine(NetworkConnectionToClient conn, C2S_BuildProp msg, ApartmentController apt) {
        // 1. Consume the delivery and capture the materialized prop UUID. The
        // DELETE endpoint returns the deleted row so we get its prop_id in the
        // same round-trip (avoids an extra GET).
        UnityWebRequest deleteReq = ApiManager.Instance.DeleteDeliveryRequest(new Delivery { _id = msg.DeliveryId });
        yield return deleteReq.SendWebRequest();

        if (deleteReq.responseCode != 200) {
            Debug.LogError($"[PropInteractionDispatcher] Delete delivery {msg.DeliveryId} failed ({deleteReq.responseCode})");
            conn.Send(new S2C_BuildAck { Success = false });
            yield break;
        }

        string propUuid = ExtractPropIdFromDeliveryResponse(deleteReq.downloadHandler?.text);

        // 2. Build initial payload — header carries presetId + isBuilt
        PropsConfig config = DatabaseManager.GetPropsById(msg.PropConfigId);
        if (config == null) {
            Debug.LogError($"[PropInteractionDispatcher] Unknown PropsConfig id={msg.PropConfigId}");
            conn.Send(new S2C_BuildAck { Success = false });
            yield break;
        }

        bool isBuilt = !config.MustBeBuilt();
        PropStateHeader header = new PropStateHeader { IsBuilt = isBuilt, PresetId = msg.PresetId };
        Debug.Log($"[Delivery] Unpack creating prop configId={msg.PropConfigId} presetId={msg.PresetId} isBuilt={isBuilt}");

        // PaintBucket needs the full payload (paint config + color); for every other
        // prop type we let the ServerPropSource emit the correct body (seat slots,
        // delivery count, ...) and only override the header to carry isBuilt/presetId.
        byte[] initialPayload = null;
        if (msg.PaintConfigId >= 0) {
            initialPayload = new PaintBucketState {
                Header        = header,
                PaintConfigId = msg.PaintConfigId,
                R = msg.ColorR, G = msg.ColorG, B = msg.ColorB
            }.Serialize();
        }

        // 3. Spawn the prop in the apartment room
        int newPropId = ServerPropManager.Instance.SpawnProp(
            apt.RoomId, msg.PropConfigId, msg.Position, msg.Rotation,
            initialPayloadOverride: initialPayload,
            headerOverride:         header,
            ownerCharId:            apt.TenantId
        );
        if (newPropId < 0) {
            conn.Send(new S2C_BuildAck { Success = false });
            yield break;
        }
        apt.TrackProp(newPropId);

        // NB : le crédit de points Ingenious + toast a été DÉPLACÉ sur l'action BUILD
        // (PropInteractionRouter.HandleGeneric / BuildRequest) — c'est la vraie
        // construction. Le placement ne fait que créer le prop (à construire si toBuild).

        // 4. Reparent server-side instance under apartment props container (for hierarchy queries)
        GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(newPropId);
        if (go != null && apt.PropsContainer != null) {
            go.transform.SetParent(apt.PropsContainer);
            go.transform.position = msg.Position;
            go.transform.rotation = msg.Rotation;
        }

        // 4b. Bridge runtime int propId → persistent UUID, and PATCH the prop's
        // new location in the DB. The buy flow created the prop in the transit
        // place; the build flow moves it to the apartment.
        if (!string.IsNullOrEmpty(propUuid)) {
            ServerPropManager.Instance.AssociateUuid(newPropId, propUuid);
            yield return PatchBuiltPropLocation(propUuid, apt, msg, isBuilt);
        }

        // 5. Refresh delivery box count (the build consumed a delivery)
        if (msg.DeliveryBoxPropId > 0) {
            string tenantCharId = apt.TenantId;
            if (!string.IsNullOrEmpty(tenantCharId)) {
                Debug.Log($"[PropInteractionDispatcher] BuildProp consumed delivery — refreshing box {msg.DeliveryBoxPropId} for tenant {tenantCharId} room={apt.RoomId}");
                RefreshDeliveryBoxCount(msg.DeliveryBoxPropId, apt.RoomId, tenantCharId);
            } else {
                Debug.LogWarning($"[PropInteractionDispatcher] BuildProp: skipping delivery box refresh — apt.TenantId empty (apt={apt.ApartmentKey} room={apt.RoomId})");
            }
        } else {
            Debug.LogWarning($"[PropInteractionDispatcher] BuildProp: skipping delivery box refresh — msg.DeliveryBoxPropId={msg.DeliveryBoxPropId} (client did not provide a box id)");
        }

        // 6. Notify the requesting client
        if (conn != null && conn.isReady) {
            conn.Send(new S2C_BuildAck { Success = true });
        }
    }

    /// <summary>
    /// Parse the deleted delivery row returned by DELETE /deliveries/:id and
    /// extract the linked prop UUID. Returns null if the delivery row carries no
    /// prop_id — the build then runs without DB persistence for that prop.
    /// </summary>
    private static string ExtractPropIdFromDeliveryResponse(string body) {
        if (string.IsNullOrEmpty(body)) return null;
        try {
            var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (dict == null) return null;
            // Backend returns the Delivery shape: { _id, recipientId, type, ..., propId }
            return dict.TryGetValue("propId", out object id) ? id?.ToString() : null;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Move the bought-then-built prop from the transit place to the apartment place
    /// via PATCH /props/:id. The prop's current DB version is read first: a prop that
    /// reached transit via a player-to-player sale already has version > 1 (the sale
    /// transfer PATCHed it), so a hardcoded expectedVersion=1 would 409 and leave the
    /// bridge stale — breaking any later list/buy on the prop.
    /// </summary>
    private IEnumerator PatchBuiltPropLocation(string propUuid, ApartmentController apt, C2S_BuildProp msg, bool isBuilt) {
        string aptPlaceId = apt.HomeData?.Id;
        if (string.IsNullOrEmpty(aptPlaceId)) {
            Debug.LogWarning($"[PropInteractionDispatcher] PatchBuiltPropLocation: apartment has no HomeData.Id (apt={apt.ApartmentKey}), skipping PATCH");
            yield break;
        }

        // Read the authoritative current version.
        int currentVersion = 1;
        UnityWebRequest getReq = ApiManager.Instance.GetPropRequest(propUuid);
        yield return getReq.SendWebRequest();
        if (getReq.responseCode == 200) {
            try {
                PropJson cur = JsonConvert.DeserializeObject<PropJson>(getReq.downloadHandler.text);
                if (cur != null && cur.version > 0) currentVersion = cur.version;
            } catch { /* fall back to 1 */ }
        } else {
            Debug.LogWarning($"[PropInteractionDispatcher] GET /props/{propUuid} failed code={getReq.responseCode} — assuming version 1");
        }

        // Keep the bridge in sync with the real version before the PATCH.
        int runtimeId = FindRuntimeIdForUuid(propUuid);
        if (runtimeId > 0) ServerPropManager.Instance.AssociateUuid(runtimeId, propUuid, currentVersion);

        UpdatePropBody body = new UpdatePropBody {
            expectedVersion = currentVersion,
            placeId  = aptPlaceId,
            position = new Vector3Body(msg.Position),
            rotation = new Vector3Body(msg.Rotation.eulerAngles),
            isBuilt  = isBuilt,
        };

        UnityWebRequest req = ApiManager.Instance.UpdatePropRequest(propUuid, body);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[PropInteractionDispatcher] PATCH /props/{propUuid} failed code={req.responseCode} body={req.downloadHandler.text}");
            yield break;
        }

        // Update to the new server-reported version so subsequent PATCHes match.
        TrackVersionFromResponse(runtimeId, req.downloadHandler.text);
        Debug.Log($"[PropInteractionDispatcher] Prop {propUuid} moved from transit to apt={apt.ApartmentKey} (version {currentVersion} → +1)");
    }

    // ── Public DB-sync helpers (called from runtime state handlers) ───────────
    //
    // Each helper looks up the prop's UUID + version via ServerPropManager.GetBridge,
    // performs the PATCH/DELETE, and updates the cached version. If the prop has no
    // bridge (e.g. a freshly preset-spawned fixture not yet materialized), the
    // sync is a no-op — MaterializeUnbridgedDoors handles deferred persistence
    // at apartment load.

    public void SyncPropTransform(int propId, Vector3 position, Quaternion rotation) {
        ServerPropManager.PropDbBridge bridge = ServerPropManager.Instance.GetBridge(propId);
        if (bridge == null) return;
        StartCoroutine(PatchPropCoroutine(propId, bridge, new UpdatePropBody {
            expectedVersion = bridge.Version,
            position = new Vector3Body(position),
            rotation = new Vector3Body(rotation.eulerAngles),
        }));
    }

    public void SyncPropState(int propId, Dictionary<string, object> stateData) {
        ServerPropManager.PropDbBridge bridge = ServerPropManager.Instance.GetBridge(propId);
        if (bridge == null) return;
        StartCoroutine(PatchPropCoroutine(propId, bridge, new UpdatePropBody {
            expectedVersion = bridge.Version,
            stateData       = stateData,
        }));
    }

    /// <summary>
    /// PATCH the prop's `is_built` flag. Called when a player completes a build
    /// interaction on a prop that had `mustBeBuilt = true` and was placed in
    /// "unbuilt" state at unpackage time.
    /// </summary>
    public void SyncPropBuilt(int propId, bool isBuilt) {
        ServerPropManager.PropDbBridge bridge = ServerPropManager.Instance.GetBridge(propId);
        if (bridge == null) return;
        StartCoroutine(PatchPropCoroutine(propId, bridge, new UpdatePropBody {
            expectedVersion = bridge.Version,
            isBuilt         = isBuilt,
        }));
    }

    public void SyncPropRemove(int propId) {
        ServerPropManager.PropDbBridge bridge = ServerPropManager.Instance.GetBridge(propId);
        if (bridge == null) return;
        StartCoroutine(DeletePropCoroutine(propId, bridge.Uuid));
    }

    /// <summary>Persists the for-sale flag + price on the props row (list/unlist).</summary>
    public void SyncPropSale(int propId, bool forSale, int price) {
        ServerPropManager.PropDbBridge bridge = ServerPropManager.Instance.GetBridge(propId);
        if (bridge == null) return;
        StartCoroutine(PatchPropCoroutine(propId, bridge, new UpdatePropBody {
            expectedVersion = bridge.Version,
            forSale         = forSale,
            price           = price,
        }));
    }

    // ── Buy (player-to-player sale) ───────────────────────────────────────────

    private const double ReservationSeconds = 15.0;

    public void BuyProp(NetworkConnectionToClient conn, C2S_BuyProp msg) {
        StartCoroutine(BuyPropCoroutine(conn, msg));
    }

    private IEnumerator BuyPropCoroutine(NetworkConnectionToClient conn, C2S_BuyProp msg) {
        // Resolve the buyer.
        PlayerController buyer = conn.identity != null ? conn.identity.GetComponent<PlayerController>() : null;
        string buyerCharId = buyer?.CharacterData?.Id;
        if (buyer == null || string.IsNullOrEmpty(buyerCharId)) {
            SendBuyResult(conn, msg.PropId, false, 4);
            yield break;
        }

        // Resolve the owning apartment (seller). Props placed in an apartment are
        // owned by its tenant; the apt's room is the authoritative room for the prop.
        ApartmentController sellerApt = ServerApartmentRegistry.Instance.FindOwnerOfProp(msg.PropId);
        if (sellerApt == null) {
            Debug.LogWarning($"[Buy] No owning apartment for prop {msg.PropId}");
            SendBuyResult(conn, msg.PropId, false, 1);
            yield break;
        }
        string roomId       = sellerApt.RoomId;
        string sellerCharId = sellerApt.TenantId;

        if (!ServerPropManager.Instance.TryGetPropState(roomId, msg.PropId, out var state) || !state.ForSale) {
            SendBuyResult(conn, msg.PropId, false, 1);
            yield break;
        }
        if (buyerCharId == sellerCharId) {       // can't buy your own prop
            SendBuyResult(conn, msg.PropId, false, 1);
            yield break;
        }

        int    price   = state.Price;
        int    configId = state.PrefabId;
        int    presetId = PropStateHeader.ReadFrom(state.Payload).PresetId;
        string buyerName = buyer.CharacterData.Identity.FullName ?? "";
        string propName  = DatabaseManager.GetPropsById(configId)?.GetDisplayName();
        if (string.IsNullOrEmpty(propName)) propName = "l'objet";

        // Atomic reservation — guards the async window below against a second buyer.
        if (!ServerPropManager.Instance.TryReserve(roomId, msg.PropId, buyerCharId, buyerName, ReservationSeconds)) {
            SendBuyResult(conn, msg.PropId, false, 1);
            yield break;
        }

        // Funds check.
        PlayerBankAccount buyerBank = conn.identity.GetComponent<PlayerBankAccount>();
        if (price > 0 && (buyerBank == null || buyerBank.Money < price)) {
            ServerPropManager.Instance.ClearReservation(roomId, msg.PropId);
            SendToast(conn, $"Fonds insuffisants pour acheter {propName} ({price}).");
            SendBuyResult(conn, msg.PropId, false, 2);
            yield break;
        }

        // Need the persistent UUID to transfer ownership. Apartment props loaded from
        // DB (and props built this session) are bridged; bail out otherwise.
        ServerPropManager.PropDbBridge bridge = ServerPropManager.Instance.GetBridge(msg.PropId);
        if (bridge == null) {
            Debug.LogWarning($"[Buy] Prop {msg.PropId} has no DB bridge — cannot transfer ownership");
            ServerPropManager.Instance.ClearReservation(roomId, msg.PropId);
            SendBuyResult(conn, msg.PropId, false, 4);
            yield break;
        }
        string propUuid = bridge.Uuid;

        // 1. Transfer ownership: move the prop to the transit place, owned by the buyer,
        //    no longer for sale. Position is left stale (harmless — transit is never
        //    loaded as a room); the build flow sets a fresh position at placement.
        UnityWebRequest patch = ApiManager.Instance.UpdatePropRequest(propUuid, new UpdatePropBody {
            expectedVersion = bridge.Version,
            placeId         = ApiManager.Instance.TransitPlaceId,
            ownedBy         = buyerCharId,
            forSale         = false,
            price           = 0,
        });
        yield return patch.SendWebRequest();
        if (patch.responseCode < 200 || patch.responseCode >= 300) {
            Debug.LogWarning($"[Buy] PATCH /props/{propUuid} failed ({patch.responseCode}) {patch.downloadHandler?.text}");
            ServerPropManager.Instance.ClearReservation(roomId, msg.PropId);
            SendBuyResult(conn, msg.PropId, false, 4);
            yield break;
        }

        // 2. Generate the buyer's delivery (links the existing prop UUID — the build
        //    flow PATCHes it into the apartment, no new prop row).
        CreateDeliveryRequest deliveryReq = new CreateDeliveryRequest {
            recipientId   = buyerCharId,
            type          = DeliveryType.PROPS,
            propsConfigId = configId,
            propsPresetId = presetId,
            paintConfigId = -1,
            color         = System.Array.Empty<float>(),
            propId        = propUuid,
        };
        UnityWebRequest delReq = ApiManager.Instance.CreateDeliveryRequest(deliveryReq);
        yield return delReq.SendWebRequest();
        if (delReq.responseCode != 200 && delReq.responseCode != 201) {
            // The ownership PATCH already committed; the buyer still owns the prop in
            // transit and can recover it later. Log and continue with payment so the
            // money side stays consistent with the ownership transfer.
            Debug.LogWarning($"[Buy] POST /deliveries failed ({delReq.responseCode}) — prop {propUuid} owned by buyer in transit without delivery");
        }

        // 3. Payment + history in one step — post a ledger entry on each side. The
        //    ledger is the single source of money history (no separate transactions
        //    row). Each side records the other character as its counterparty. Gifts
        //    (price 0) still get a 0-amount entry for traceability of the transfer.
        PlayerBankAccount sellerBank = FindOnlineBankAccount(sellerCharId);
        bool isGift = price == 0;

        // Buyer side (online — this is the connection that initiated the buy).
        buyerBank.PostLedger(isGift ? 0 : -price,
            isGift ? LedgerReason.GiftReceived : LedgerReason.P2pPurchase,
            LedgerCounterparty.Player, sellerCharId, propUuid, configId);

        // Seller side — online via its bank component, offline via direct REST post.
        if (sellerBank != null) {
            sellerBank.PostLedger(isGift ? 0 : price,
                isGift ? LedgerReason.GiftSent : LedgerReason.P2pSale,
                LedgerCounterparty.Player, buyerCharId, propUuid, configId);
        } else if (!string.IsNullOrEmpty(sellerCharId)) {
            UnityWebRequest credit = ApiManager.Instance.PostLedgerEntryRequest(sellerCharId, new PostLedgerBody {
                amount           = isGift ? 0 : price,
                reason           = isGift ? LedgerReason.GiftSent : LedgerReason.P2pSale,
                counterpartyType = LedgerCounterparty.Player,
                counterpartyId   = buyerCharId,
                propId           = propUuid,
                configId         = configId,
            });
            yield return credit.SendWebRequest();
            if (credit.responseCode < 200 || credit.responseCode >= 300)
                Debug.LogWarning($"[Buy] Offline seller ledger post failed ({credit.responseCode}) seller={sellerCharId} amount={price}");
        }

        // 5. Remove the prop from the seller's room (runtime only — the DB row was
        //    transferred, NOT deleted). Clear the seller-side runtime→UUID bridge so a
        //    later FindPropIdByUuid can't resolve to this dead id (which would corrupt
        //    version tracking when the buyer rebuilds the same prop → 409 on re-buy).
        sellerApt.UntrackProp(msg.PropId);
        ServerPropManager.Instance.RemoveProp(roomId, msg.PropId);
        ServerPropManager.Instance.ClearBridge(msg.PropId);

        SendBuyResult(conn, msg.PropId, true, 0);

        // 5b. Notify both parties (the seller only if online).
        SendToast(conn, price > 0 ? $"Tu as acheté {propName} pour {price}." : $"Tu as reçu {propName}.");
        if (sellerBank != null)
            SendToast(sellerBank.connectionToClient,
                price > 0 ? $"Tu as vendu {propName} pour {price}." : $"Tu as donné {propName}.");

        // 6. Refresh the buyer's delivery box if their apartment is currently loaded.
        if (ServerApartmentRegistry.Instance.TryGetByTenant(buyerCharId, out ApartmentController buyerApt)
            && buyerApt.DeliveryBoxPropId > 0)
            RefreshDeliveryBoxCount(buyerApt.DeliveryBoxPropId, buyerApt.RoomId, buyerCharId);

        Debug.Log($"[Buy] Prop {propUuid} sold: seller={sellerCharId} buyer={buyerCharId} price={price}");
    }

    // ── Buy (magasin physique — copie, l'expo reste) ──────────────────────────

    public void BuyShopDisplay(NetworkConnectionToClient conn, C2S_BuyProp msg) {
        StartCoroutine(BuyShopDisplayCoroutine(conn, msg));
    }

    /// <summary>
    /// Achat d'un prop d'exposition d'un magasin physique : on crée une COPIE livrée
    /// à l'acheteur (flux phone shop : POST /props transit + POST /deliveries + ledger)
    /// et le prop d'expo reste en place (stock infini). Pas de réservation ni de transfert.
    /// </summary>
    private IEnumerator BuyShopDisplayCoroutine(NetworkConnectionToClient conn, C2S_BuyProp msg) {
        // Acheteur.
        PlayerController buyer = conn.identity != null ? conn.identity.GetComponent<PlayerController>() : null;
        string buyerCharId = buyer?.CharacterData?.Id;
        if (buyer == null || string.IsNullOrEmpty(buyerCharId)) {
            SendBuyResult(conn, msg.PropId, false, 4);
            yield break;
        }

        // Le prop d'expo doit exister, être un ShopDisplay et en vente.
        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var state)
            || !state.IsShopDisplay || !state.ForSale) {
            SendBuyResult(conn, msg.PropId, false, 1);
            yield break;
        }

        int    price    = state.Price;            // déjà remisé à l'enregistrement
        int    configId = state.PrefabId;
        int    presetId = PropStateHeader.ReadFrom(state.Payload).PresetId;
        string propName = DatabaseManager.GetPropsById(configId)?.GetDisplayName();
        if (string.IsNullOrEmpty(propName)) propName = "l'objet";

        // Fonds.
        PlayerBankAccount buyerBank = conn.identity.GetComponent<PlayerBankAccount>();
        if (price > 0 && (buyerBank == null || buyerBank.Money < price)) {
            SendToast(conn, $"Fonds insuffisants pour acheter {propName} ({price}).");
            SendBuyResult(conn, msg.PropId, false, 2);
            yield break;
        }

        // 1. Matérialiser une COPIE en transit, possédée par l'acheteur.
        string transitId = ApiManager.Instance.TransitPlaceId;
        if (string.IsNullOrEmpty(transitId)) {
            Debug.LogWarning("[ShopBuy] transit place not ready");
            SendBuyResult(conn, msg.PropId, false, 4);
            yield break;
        }
        CreatePropBody body = ShopPurchaseHelper.BuildTransitPropBody(transitId, configId, buyerCharId, presetId);
        UnityWebRequest createReq = ApiManager.Instance.CreatePropRequest(body);
        yield return createReq.SendWebRequest();
        if (createReq.responseCode != 200 && createReq.responseCode != 201) {
            Debug.LogWarning($"[ShopBuy] POST /props failed ({createReq.responseCode}) {createReq.downloadHandler?.text}");
            SendBuyResult(conn, msg.PropId, false, 4);
            yield break;
        }
        string propUuid = null;
        try {
            PropJson created = JsonConvert.DeserializeObject<PropJson>(createReq.downloadHandler.text);
            propUuid = created?.Id;
        } catch { /* propUuid null toléré : la livraison sera créée sans lien */ }

        // 2. Générer la livraison vers l'acheteur (le build flow PATCHera la copie en place).
        CreateDeliveryRequest deliveryReq = new CreateDeliveryRequest {
            recipientId   = buyerCharId,
            type          = DeliveryType.PROPS,
            propsConfigId = configId,
            propsPresetId = presetId,
            paintConfigId = -1,
            color         = System.Array.Empty<float>(),
            propId        = propUuid,
        };
        UnityWebRequest delReq = ApiManager.Instance.CreateDeliveryRequest(deliveryReq);
        yield return delReq.SendWebRequest();
        if (delReq.responseCode != 200 && delReq.responseCode != 201) {
            Debug.LogWarning($"[ShopBuy] POST /deliveries failed ({delReq.responseCode}) — prop {propUuid} possédé en transit sans livraison");
        }

        // 3. Débit acheteur (counterparty = SHOP) — même chokepoint que le phone shop.
        if (buyerBank != null)
            buyerBank.PostLedger(-price, LedgerReason.ShopPurchase, LedgerCounterparty.System,
                LedgerCounterparty.Shop, configId: configId);

        // 4. L'expo reste en place : aucune réservation, aucun RemoveProp.
        SendBuyResult(conn, msg.PropId, true, 0);
        SendToast(conn, price > 0 ? $"Tu as acheté {propName} pour {price}." : $"Tu as reçu {propName}.");

        // 5. Rafraîchir la delivery box de l'acheteur si son appart est chargé.
        if (ServerApartmentRegistry.Instance.TryGetByTenant(buyerCharId, out ApartmentController buyerApt)
            && buyerApt.DeliveryBoxPropId > 0)
            RefreshDeliveryBoxCount(buyerApt.DeliveryBoxPropId, buyerApt.RoomId, buyerCharId);

        Debug.Log($"[ShopBuy] Copie créée prop={propUuid} config={configId} buyer={buyerCharId} price={price}");
    }

    private static void SendBuyResult(NetworkConnectionToClient conn, int propId, bool success, byte reason) {
        if (conn != null && conn.isReady)
            conn.Send(new S2C_BuyPropResult { PropId = propId, Success = success, ReasonCode = reason });
    }

    private static void SendToast(NetworkConnectionToClient conn, string text, ToastKind kind = ToastKind.Neutral) {
        if (conn != null && conn.isReady)
            conn.Send(new ToastNotificationMessage {
                text = text, typeByte = (byte)NotificationType.BANK, worldToast = true, kindByte = (byte)kind });
    }

    /// <summary>Finds the bank account of an online character by id, or null if offline.</summary>
    private static PlayerBankAccount FindOnlineBankAccount(string charId) {
        if (string.IsNullOrEmpty(charId)) return null;
        foreach (var kv in NetworkServer.spawned) {
            PlayerController pc = kv.Value != null ? kv.Value.GetComponent<PlayerController>() : null;
            if (pc != null && pc.CharacterData?.Id == charId)
                return pc.GetComponent<PlayerBankAccount>();
        }
        return null;
    }

    public void SyncCovers(string placeId, IEnumerable<CoverApplyEntry> covers) {
        if (string.IsNullOrEmpty(placeId) || covers == null) return;
        StartCoroutine(UpsertCoversCoroutine(placeId, covers));
    }

    /// <summary>Lightweight DTO used by SyncCovers callers.</summary>
    public struct CoverApplyEntry {
        public string SurfaceKind;        // "wall" | "ground"
        public int    SurfaceIndex;
        public int    PaintConfigId;
        public float[] Color;
    }

    private IEnumerator PatchPropCoroutine(int propId, ServerPropManager.PropDbBridge bridge, UpdatePropBody body) {
        UnityWebRequest req = ApiManager.Instance.UpdatePropRequest(bridge.Uuid, body);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[PropInteractionDispatcher] PATCH /props/{bridge.Uuid} failed code={req.responseCode} body={req.downloadHandler.text}");
            yield break;
        }
        TrackVersionFromResponse(propId, req.downloadHandler.text);
    }

    private IEnumerator DeletePropCoroutine(int propId, string propUuid) {
        UnityWebRequest req = ApiManager.Instance.DeletePropRequest(propUuid);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[PropInteractionDispatcher] DELETE /props/{propUuid} failed code={req.responseCode} body={req.downloadHandler.text}");
            yield break;
        }
        ServerPropManager.Instance.ClearBridge(propId);
        Debug.Log($"[PropInteractionDispatcher] Prop {propUuid} deleted from DB");
    }

    private IEnumerator UpsertCoversCoroutine(string placeId, IEnumerable<CoverApplyEntry> covers) {
        var payload = new {
            covers = System.Linq.Enumerable.Select(covers, c => new {
                surfaceKind   = c.SurfaceKind,
                surfaceIndex  = c.SurfaceIndex,
                paintConfigId = c.PaintConfigId,
                color         = c.Color ?? new float[] { 1, 1, 1, 1 },
            }).ToArray()
        };
        UnityWebRequest req = ApiManager.Instance.UpsertCoversRequest(placeId, payload);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[PropInteractionDispatcher] PUT /places/{placeId}/covers failed code={req.responseCode} body={req.downloadHandler.text}");
        }
    }

    private void TrackVersionFromResponse(int propId, string body) {
        if (propId <= 0 || string.IsNullOrEmpty(body)) return;
        try {
            PropJson updated = JsonConvert.DeserializeObject<PropJson>(body);
            if (updated != null) ServerPropManager.Instance.UpdateVersion(propId, updated.version);
        } catch { /* tolerated; next PATCH will fail and we'll re-sync */ }
    }

    private static int FindRuntimeIdForUuid(string uuid) =>
        ServerPropManager.Instance.FindPropIdByUuid(uuid);
}
