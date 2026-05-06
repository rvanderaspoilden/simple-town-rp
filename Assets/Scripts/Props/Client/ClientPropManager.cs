using System.Collections.Generic;
using Mirror;
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
public class ClientPropManager : MonoBehaviour {
    public static ClientPropManager Instance { get; private set; }

    private string _currentRoomId;

    // propId → behaviour (scene OR runtime)
    private readonly Dictionary<int, IPropBehaviour> _props = new Dictionary<int, IPropBehaviour>();

    // propId → GameObject for runtime-spawned props only (we own these and Destroy on remove).
    private readonly Dictionary<int, GameObject> _spawnedGOs = new Dictionary<int, GameObject>();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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
    }

    public static event System.Action<bool> OnBuildAckReceived;

    // ── Room entry / exit ─────────────────────────────────────────────────────

    /// <summary>
    /// Call when the local player transitions into a new room.
    /// Clears previous indexing, scans the local scene for scene props in
    /// the new room, then asks the server for the snapshot.
    /// </summary>
    public void EnterRoom(string roomId) {
        if (_currentRoomId == roomId) return;

        if (!string.IsNullOrEmpty(_currentRoomId)) {
            NetworkClient.Send(new C2S_LeaveRoom { RoomId = _currentRoomId });
        }
        ClearProps();

        _currentRoomId = roomId;
        IndexSceneProps(roomId);

        NetworkClient.Send(new C2S_EnterRoom { RoomId = roomId });
        Debug.Log($"[ClientPropManager] Entering room '{roomId}' (indexed {_props.Count} scene props)");
    }

    // ── Interaction dispatch ──────────────────────────────────────────────────

    public void RequestInteraction(int propId, PropType type, byte[] payload) {
        if (string.IsNullOrEmpty(_currentRoomId)) return;
        NetworkClient.Send(new C2S_PropInteraction {
            PropId  = propId,
            RoomId  = _currentRoomId,
            Type    = type,
            Payload = payload
        });
    }

    // ── S2C handlers ──────────────────────────────────────────────────────────

    private void OnRoomSnapshot(S2C_RoomSnapshot msg) {
        if (msg.RoomId != _currentRoomId) return;
        Debug.Log($"[ClientPropManager] Snapshot for '{msg.RoomId}' ({msg.PropCount} props)");
    }

    private void OnPropSpawn(S2C_PropSpawn msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.ContainsKey(msg.PropId)) return;

        var propsConfig = Sim.DatabaseManager.PropsDatabase?.GetPropsById(msg.PrefabId);
        GameObject prefab = propsConfig?.GetPrefab()?.gameObject;
        if (prefab == null) {
            Debug.LogWarning($"[ClientPropManager] PropsConfig id={msg.PrefabId} not found — prop {msg.PropId} skipped");
            return;
        }

        GameObject go = Instantiate(prefab, msg.Position, msg.Rotation);
        go.name = $"Prop{msg.PrefabId}#{msg.PropId}";

        PropIdentity identity = go.GetComponent<PropIdentity>();
        identity?.Assign(msg.PropId, msg.RoomId);

        IPropBehaviour behaviour = go.GetComponent<IPropBehaviour>();
        if (behaviour != null) {
            _props[msg.PropId] = behaviour;
            behaviour.ApplyState(msg.Type, msg.Payload);
        }
        _spawnedGOs[msg.PropId] = go;
    }

    private void OnPropUpdate(S2C_PropUpdate msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.TryGetValue(msg.PropId, out var behaviour)) {
            behaviour.ApplyState(msg.Type, msg.Payload);
        }
    }

    private void OnPropTransform(S2C_PropTransform msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_spawnedGOs.TryGetValue(msg.PropId, out var go) && go != null) {
            go.transform.position = msg.Position;
            go.transform.rotation = msg.Rotation;
        }
    }

    private void OnBuildAck(S2C_BuildAck msg) {
        OnBuildAckReceived?.Invoke(msg.Success);
    }

    private void OnPropRemove(S2C_PropRemove msg) {
        if (msg.RoomId != _currentRoomId) return;
        _props.Remove(msg.PropId);
        if (_spawnedGOs.TryGetValue(msg.PropId, out var go)) {
            if (go != null) Destroy(go);
            _spawnedGOs.Remove(msg.PropId);
        }
    }

    private void OnDeliveryBoxOpened(S2C_DeliveryBoxOpened msg) {
        if (msg.RoomId != _currentRoomId) return;
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is DeliveryBoxBehaviour box) {
            box.OnDeliveryBoxOpened(msg.Deliveries);
        }
    }

    private void OnDispenserPurchaseResult(S2C_DispenserPurchaseResult msg) {
        if (_props.TryGetValue(msg.PropId, out var behaviour) && behaviour is DispenserBehaviour disp) {
            disp.HandlePurchaseResult(msg.Success, msg.ItemId);
        }
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
