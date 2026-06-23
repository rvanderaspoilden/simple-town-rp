using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using UnityEngine;

/// <summary>
/// Client-side prop manager.
///
/// Two prop origins:
///   - Scene props (city)   : prefab placed in City.unity. PropIdentity holds
///                            propId/roomId. On EnterRoom the local scene is
///                            scanned and indexed; their state is then applied
///                            from S2C_PropUpdate messages in the snapshot.
///   - Runtime props (apt.) : received via S2C_PropSpawn — instantiated locally,
///                            PropIdentity assigned, indexed, state applied.
///
/// All routing is by propId (no prefab ↔ scene-object lookup).
/// </summary>
[DefaultExecutionOrder(-100)] // Ensure this initializes before NetworkManager callbacks
public class ClientPropManager : MonoBehaviour {
    public static ClientPropManager Instance { get; private set; }

    private string _currentRoomId;

    /// <summary>Current local room id ("city" outdoors, "hall:..."/"apartment:..." indoors, null before any EnterRoom).</summary>
    public string CurrentRoomId => _currentRoomId;

    // propId → behaviour (scene OR runtime)
    private readonly Dictionary<int, IPropBehaviour> _props = new Dictionary<int, IPropBehaviour>();

    // propId → GameObject for runtime-spawned props only (we own these and Destroy on remove).
    private readonly Dictionary<int, GameObject> _spawnedGOs = new Dictionary<int, GameObject>();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ClientLogger.Network("ClientPropManagerInitialized");
    }

    // ── Mirror handler registration ───────────────────────────────────────────

    public void RegisterHandlers() {
        NetworkClient.RegisterHandler<S2C_RoomSnapshot>           (OnRoomSnapshot);
        NetworkClient.RegisterHandler<S2C_PropSpawn>              (OnPropSpawn);
        NetworkClient.RegisterHandler<S2C_PropUpdate>             (OnPropUpdate);
        NetworkClient.RegisterHandler<S2C_PropTransform>          (OnPropTransform);
        NetworkClient.RegisterHandler<S2C_PropRemove>             (OnPropRemove);
        NetworkClient.RegisterHandler<S2C_DeliveryBoxOpened>      (OnDeliveryBoxOpened);
        NetworkClient.RegisterHandler<S2C_DispenserPurchaseResult>(OnDispenserPurchaseResult);
        NetworkClient.RegisterHandler<S2C_BuildAck>               (OnBuildAck);
        NetworkClient.RegisterHandler<S2C_RoomState>              (OnRoomState);
        NetworkClient.RegisterHandler<S2C_DoorRing>               (OnDoorRing);
        NetworkClient.RegisterHandler<S2C_TrashThrown>           (OnTrashThrown);
        NetworkClient.RegisterHandler<S2C_ConstructionVfx>       (OnConstructionVfx);
        NetworkClient.RegisterHandler<S2C_PropDestroyed>         (OnPropDestroyed);
        NetworkClient.RegisterHandler<S2C_PropPacked>            (OnPropPacked);
        NetworkClient.RegisterHandler<S2C_PropSaleState>          (OnPropSaleState);
        NetworkClient.RegisterHandler<S2C_BuyPropResult>          (OnBuyPropResult);
        NetworkClient.RegisterHandler<S2C_ContainerVisualState>   (OnContainerVisualState);
        ClientLogger.NetworkDebug("ClientPropHandlersRegistered {Count}", 14);
    }

    public void UnregisterHandlers() {
        NetworkClient.UnregisterHandler<S2C_RoomSnapshot>();
        NetworkClient.UnregisterHandler<S2C_PropSpawn>();
        NetworkClient.UnregisterHandler<S2C_PropUpdate>();
        NetworkClient.UnregisterHandler<S2C_PropTransform>();
        NetworkClient.UnregisterHandler<S2C_PropRemove>();
        NetworkClient.UnregisterHandler<S2C_DeliveryBoxOpened>();
        NetworkClient.UnregisterHandler<S2C_DispenserPurchaseResult>();
        NetworkClient.UnregisterHandler<S2C_BuildAck>();
        NetworkClient.UnregisterHandler<S2C_RoomState>();
        NetworkClient.UnregisterHandler<S2C_DoorRing>();
        NetworkClient.UnregisterHandler<S2C_TrashThrown>();
        NetworkClient.UnregisterHandler<S2C_ConstructionVfx>();
        NetworkClient.UnregisterHandler<S2C_PropDestroyed>();
        NetworkClient.UnregisterHandler<S2C_PropPacked>();
        NetworkClient.UnregisterHandler<S2C_PropSaleState>();
        NetworkClient.UnregisterHandler<S2C_BuyPropResult>();
        NetworkClient.UnregisterHandler<S2C_ContainerVisualState>();
        ClientLogger.NetworkDebug("ClientPropHandlersUnregistered");
    }

    public static event System.Action<bool>           OnBuildAckReceived;
    public static event System.Action<string, byte[]> OnRoomStateReceived;
    public static event System.Action<string>         OnLocalRoomChanged;

    /// <summary>Fired on the buyer's client after a buy attempt. (propId, success, reasonCode).</summary>
    public static event System.Action<int, bool, byte> OnBuyResultReceived;

    // ── Room entry / exit ─────────────────────────────────────────────────────

    /// <summary>
    /// Call when the local player transitions into a new room.
    /// Clears previous indexing, scans the local scene for scene props in
    /// the new room, then asks the server for the snapshot.
    /// </summary>
    public void EnterRoom(string roomId) {
        if (_currentRoomId == roomId) {
            ClientLogger.NetworkDebug("EnterRoomSkipped {RoomId} (already in room)", roomId);
            return;
        }

        string oldRoomId = _currentRoomId;
        if (!string.IsNullOrEmpty(_currentRoomId)) {
            ClientLogger.Network("LeaveRoomRequest {RoomId}", _currentRoomId);
            NetworkClient.Send(new C2S_LeaveRoom { RoomId = _currentRoomId });
        }
        ClearProps();

        _currentRoomId = roomId;
        IndexSceneProps(roomId);

        NetworkClient.Send(new C2S_EnterRoom { RoomId = roomId });
        OnLocalRoomChanged?.Invoke(roomId);
        ClientLogger.Network("EnterRoomRequest {RoomId} {OldRoomId} {ScenePropCount}", roomId, oldRoomId ?? "none", _props.Count);
    }

    // ── Interaction dispatch ──────────────────────────────────────────────────

    public void RequestInteraction(int propId, PropType type, byte[] payload) {
        if (string.IsNullOrEmpty(_currentRoomId)) {
            ClientLogger.NetworkWarning("PropInteractionNoRoom {PropId} {Type}", propId, type);
            return;
        }
        ClientLogger.NetworkDebug("PropInteractionRequest {RoomId} {PropId} {Type} {PayloadSize}",
            _currentRoomId, propId, type, payload?.Length ?? 0);
        NetworkClient.Send(new C2S_PropInteraction {
            PropId  = propId,
            RoomId  = _currentRoomId,
            Type    = type,
            Payload = payload
        });
    }

    // ── Sale requests ─────────────────────────────────────────────────────────

    public void RequestSetForSale(int propId, int price) {
        if (string.IsNullOrEmpty(_currentRoomId)) return;
        NetworkClient.Send(new C2S_SetPropForSale { RoomId = _currentRoomId, PropId = propId, Price = Mathf.Max(0, price) });
    }

    public void RequestUnlist(int propId) {
        if (string.IsNullOrEmpty(_currentRoomId)) return;
        NetworkClient.Send(new C2S_UnlistProp { RoomId = _currentRoomId, PropId = propId });
    }

    public void RequestBuy(int propId) {
        if (string.IsNullOrEmpty(_currentRoomId)) return;
        NetworkClient.Send(new C2S_BuyProp { RoomId = _currentRoomId, PropId = propId });
    }

    /// <summary>Demande de remballer un prop non construit dans le package ouvert.</summary>
    public void RequestPackProp(int propId) {
        NetworkClient.Send(new C2S_PackProp { PropId = propId });
    }

    /// <summary>Signal a construction-VFX phase for a prop (0 = start, 1 = finale, 2 = cancel).</summary>
    public void RequestConstructionVfx(int propId, byte phase, int durationMs = 0) {
        if (string.IsNullOrEmpty(_currentRoomId)) return;
        NetworkClient.Send(new C2S_ConstructionVfx { RoomId = _currentRoomId, PropId = propId, Phase = phase, DurationMs = durationMs });
    }

    // ── S2C handlers ──────────────────────────────────────────────────────────

    private void OnRoomSnapshot(S2C_RoomSnapshot msg) {
        if (msg.RoomId != _currentRoomId) {
            ClientLogger.NetworkDebug("RoomSnapshotIgnored {MsgRoomId} {CurrentRoomId}", msg.RoomId, _currentRoomId);
            return;
        }
        Debug.Log($"[Hall] Received hall snapshot room={msg.RoomId} propCount={msg.PropCount}");
        ClientLogger.Network("RoomSnapshotReceived {RoomId} {PropCount}", msg.RoomId, msg.PropCount);
    }

    private void OnPropSpawn(S2C_PropSpawn msg) {
        if (msg.RoomId != _currentRoomId) {
            ClientLogger.NetworkDebug("PropSpawnIgnored {PropId} {MsgRoomId} {CurrentRoomId}",
                msg.PropId, msg.RoomId, _currentRoomId);
            return;
        }
        if (_props.ContainsKey(msg.PropId)) {
            // Already indexed (scene props or previously spawned). Apply the authoritative state.
            if (_props[msg.PropId] is PropBehaviourBase pbExisting) pbExisting.SetOwner(msg.OwnerCharId);
            _props[msg.PropId].ApplyState(msg.Type, msg.Payload);
            ClientLogger.NetworkDebug("PropSpawnExistingStateApplied {PropId} {RoomId}", msg.PropId, msg.RoomId);
            return;
        }

        // Host mode: the server already instantiated the GameObject. Reuse it instead of
        // instantiating a duplicate that would overlap and confuse the renderer state.
        if (NetworkServer.active) {
            GameObject hostGo = ServerPropManager.Instance.GetSpawnedGameObject(msg.PropId);
            if (hostGo != null) {
                IPropBehaviour hostBehaviour = hostGo.GetComponent<IPropBehaviour>();
                if (hostBehaviour != null) {
                    _props[msg.PropId] = hostBehaviour;
                    _spawnedGOs[msg.PropId] = hostGo;
                    if (hostBehaviour is PropBehaviourBase pbHost) pbHost.SetOwner(msg.OwnerCharId);
                    PropStateHeader hostHeader = PropStateHeader.ReadFrom(msg.Payload);
                    Debug.Log($"[PropSpawn] (host) Reusing server GO propId={msg.PropId} prefabId={msg.PrefabId} presetId={hostHeader.PresetId} isBuilt={hostHeader.IsBuilt}");
                    hostBehaviour.ApplyState(msg.Type, msg.Payload);
                }
                ClientLogger.NetworkDebug("PropSpawnHostReused {PropId} {PrefabId} {RoomId}", msg.PropId, msg.PrefabId, msg.RoomId);
                return;
            }
        }

        var propsConfig = Sim.DatabaseManager.GetPropsById(msg.PrefabId);
        GameObject prefab = propsConfig?.GetPrefab()?.gameObject;
        if (prefab == null) {
            ClientLogger.NetworkWarning("PropSpawnConfigNotFound {PropId} {PrefabId}", msg.PropId, msg.PrefabId);
            return;
        }

        GameObject go = Instantiate(prefab, msg.Position, msg.Rotation);
        go.name = $"Prop{msg.PrefabId}#{msg.PropId}";

        PropIdentity identity = go.GetComponent<PropIdentity>();
        identity?.Assign(msg.PropId, msg.RoomId);

        IPropBehaviour behaviour = go.GetComponent<IPropBehaviour>();
        if (behaviour != null) {
            _props[msg.PropId] = behaviour;
            if (behaviour is PropBehaviourBase pbNew) pbNew.SetOwner(msg.OwnerCharId);
            PropStateHeader header = PropStateHeader.ReadFrom(msg.Payload);
            Debug.Log($"[PropSpawn] Received prop propId={msg.PropId} prefabId={msg.PrefabId} presetId={header.PresetId} isBuilt={header.IsBuilt}");
            behaviour.ApplyState(msg.Type, msg.Payload);
        }
        _spawnedGOs[msg.PropId] = go;

        ClientLogger.NetworkDebug("PropSpawned {PropId} {PrefabId} {RoomId}", msg.PropId, msg.PrefabId, msg.RoomId);
    }

    private void OnPropUpdate(S2C_PropUpdate msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.TryGetValue(msg.PropId, out var behaviour)) {
            behaviour.ApplyState(msg.Type, msg.Payload);
            ClientLogger.NetworkDebug("PropUpdated {PropId} {RoomId} {Type}", msg.PropId, msg.RoomId, msg.Type);
        } else {
            ClientLogger.NetworkDebug("PropUpdateUnknownProp {PropId} {RoomId}", msg.PropId, msg.RoomId);
        }
    }

    private void OnPropTransform(S2C_PropTransform msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_spawnedGOs.TryGetValue(msg.PropId, out var go) && go != null) {
            go.transform.position = msg.Position;
            go.transform.rotation = msg.Rotation;
            ClientLogger.NetworkDebug("PropTransformUpdated {PropId} {Position}", msg.PropId, msg.Position);
        }
    }

    private void OnBuildAck(S2C_BuildAck msg) {
        ClientLogger.Network("BuildAck {Success}", msg.Success);
        OnBuildAckReceived?.Invoke(msg.Success);
    }

    private void OnDoorRing(S2C_DoorRing msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is DoorBehaviour door) {
            door.PlayRingSound();
        }
        ClientLogger.NetworkDebug("DoorRing {PropId} {RoomId}", msg.PropId, msg.RoomId);
    }

    private void OnTrashThrown(S2C_TrashThrown msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is TrashBehaviour trash) {
            bool byLocal = NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == msg.ThrowerNetId;
            trash.OnThrown(byLocal);
        }
        ClientLogger.NetworkDebug("TrashThrown {PropId} {RoomId}", msg.PropId, msg.RoomId);
    }

    private void OnConstructionVfx(S2C_ConstructionVfx msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is PropBehaviourBase prop) {
            prop.ApplyConstructionVfx(msg.Phase, msg.DurationMs);
        }
        ClientLogger.NetworkDebug("ConstructionVfx {PropId} {Phase}", msg.PropId, msg.Phase);
    }

    private void OnPropDestroyed(S2C_PropDestroyed msg) {
        if (msg.RoomId != _currentRoomId) return;
        DestructionVfx.SpawnAt(msg.Position);
        Sim.Audio.AudioManager.Instance.Play(Sim.Audio.SfxId.PropDestroy, msg.Position);
        ClientLogger.NetworkDebug("PropDestroyed {RoomId} {Position}", msg.RoomId, msg.Position);
    }

    private void OnPropPacked(S2C_PropPacked msg) {
        if (msg.RoomId != _currentRoomId) return;
        PackVfx.SpawnAt(msg.Position);
        ClientLogger.NetworkDebug("PropPacked {RoomId} {Position}", msg.RoomId, msg.Position);
    }

    private void OnRoomState(S2C_RoomState msg) {
        ClientLogger.NetworkDebug("RoomStateReceived {RoomId} {PayloadSize}", msg.RoomId, msg.Payload?.Length ?? 0);
        Debug.Log($"[RoomSnapshot] Finished entity reconstruction room={msg.RoomId} props={_props.Count}");
        OnRoomStateReceived?.Invoke(msg.RoomId, msg.Payload);
    }

    private void OnPropRemove(S2C_PropRemove msg) {
        if (msg.RoomId != _currentRoomId) return;
        _props.Remove(msg.PropId);
        if (_spawnedGOs.TryGetValue(msg.PropId, out var go)) {
            if (go != null) {
                Destroy(go);
                ClientLogger.NetworkDebug("PropRemoved {PropId} {RoomId}", msg.PropId, msg.RoomId);
            }
            _spawnedGOs.Remove(msg.PropId);
        }
    }

    private void OnDeliveryBoxOpened(S2C_DeliveryBoxOpened msg) {
        if (msg.RoomId != _currentRoomId) return;
        ClientLogger.Network("DeliveryBoxOpened {PropId} {RoomId} {DeliveryCount}",
            msg.PropId, msg.RoomId, msg.Deliveries?.Length ?? 0);
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is DeliveryBoxBehaviour box) {
            box.OnDeliveryBoxOpened(msg.Deliveries);
        }
    }

    private void OnPropSaleState(S2C_PropSaleState msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is PropBehaviourBase prop) {
            prop.ApplySaleState(msg.ForSale, msg.Price, msg.ReservedByName, msg.OwnerCharId);
            ClientLogger.NetworkDebug("PropSaleState {PropId} {ForSale} {Price}", msg.PropId, msg.ForSale, msg.Price);
        }
    }

    private void OnBuyPropResult(S2C_BuyPropResult msg) {
        ClientLogger.Network("BuyPropResult {PropId} {Success} {Reason}", msg.PropId, msg.Success, msg.ReasonCode);
        OnBuyResultReceived?.Invoke(msg.PropId, msg.Success, msg.ReasonCode);
    }

    private void OnDispenserPurchaseResult(S2C_DispenserPurchaseResult msg) {
        ClientLogger.Network("DispenserPurchaseResult {PropId} {Success} {ItemId}",
            msg.PropId, msg.Success, msg.ItemId);
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is DispenserBehaviour disp) {
            disp.HandlePurchaseResult(msg.Success, msg.ItemId);
        }
    }

    private void OnContainerVisualState(S2C_ContainerVisualState msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is StorageContainerBehaviour storage) {
            storage.SetOpenState(msg.IsOpen);
        }
        ClientLogger.NetworkDebug("ContainerVisualState {PropId} {IsOpen}", msg.PropId, msg.IsOpen);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void IndexSceneProps(string roomId) {
        foreach (PropIdentity id in FindObjectsByType<PropIdentity>(FindObjectsSortMode.None)) {
            if (id.RoomId != roomId || id.PropId <= 0) continue;
            IPropBehaviour behaviour = id.GetComponent<IPropBehaviour>();
            if (behaviour == null) continue;
            if (!_props.ContainsKey(id.PropId)) _props[id.PropId] = behaviour;
        }
    }

    private void ClearProps() {
        foreach (var kv in _spawnedGOs) {
            if (kv.Value != null) Destroy(kv.Value);
        }
        _spawnedGOs.Clear();
        _props.Clear();
    }
}
