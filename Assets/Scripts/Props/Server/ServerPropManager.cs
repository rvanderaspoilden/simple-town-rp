using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Authoritative, server-side store for all prop states grouped by room.
/// Mirror is used only as a transport — no SyncVar, no NetworkBehaviour, no NetworkIdentity.
///
/// Two registration paths:
///   - RegisterSceneProp(source) : for props placed in the scene (City).
///                                 No broadcast — clients have the GO via scene load.
///   - SpawnProp(prefabId, ...)  : for runtime-spawned props (apartment furniture).
///                                 Instantiates the prefab server-side, assigns a propId,
///                                 records the state, broadcasts S2C_PropSpawn.
/// </summary>
public class ServerPropManager {
    private static ServerPropManager _instance;
    public static ServerPropManager Instance => _instance ??= new ServerPropManager();

    private readonly Dictionary<string, Dictionary<int, ServerPropState>> _rooms
        = new Dictionary<string, Dictionary<int, ServerPropState>>();

    // Runtime-spawned GameObjects (server-side instances). Scene props are not tracked here.
    private readonly Dictionary<int, GameObject> _spawnedGOs = new Dictionary<int, GameObject>();

    private int _nextAutoId = 10000; // runtime IDs start high to avoid collisions with scene IDs (1..9999)

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Reset() {
        foreach (var go in _spawnedGOs.Values) {
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        _spawnedGOs.Clear();
        _rooms.Clear();
        _nextAutoId = 10000;
    }

    // ── Scene props ───────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the initial state of a prop already present in the scene.
    /// No network broadcast — clients have the GameObject via scene load and
    /// will receive its current state through the next room snapshot.
    /// </summary>
    public void RegisterSceneProp(ServerPropSource source) {
        if (source == null) return;
        if (source.PropId <= 0) {
            Debug.LogWarning($"[ServerPropManager] Scene prop on '{source.gameObject.name}' has no propId — skipped");
            return;
        }

        // Get PropsConfig from PropBehaviourBase to store PrefabId
        int prefabId = 0;
        PropBehaviourBase behaviour = source.GetComponent<PropBehaviourBase>();
        if (behaviour != null && behaviour.GetConfiguration() != null) {
            prefabId = behaviour.GetConfiguration().GetId();
        }

        RegisterInternal(
            roomId:   source.RoomId,
            propId:   source.PropId,
            prefabId: prefabId,
            position: source.transform.position,
            rotation: source.transform.rotation,
            type:     source.Type,
            payload:  source.GetInitialState(),
            isScene:  true
        );
    }

    // ── Runtime spawning ──────────────────────────────────────────────────────

    /// <summary>
    /// Instantiates a prop prefab server-side, assigns a propId via PropIdentity,
    /// records its state, and broadcasts S2C_PropSpawn to current room members.
    /// Returns the assigned propId (or -1 on failure).
    /// </summary>
    public int SpawnProp(
        string     roomId,
        int        prefabId,
        Vector3    position,
        Quaternion rotation,
        byte[]     initialPayloadOverride = null
    ) {
        if (!NetworkServer.active) return -1;

        var propsConfig = Sim.DatabaseManager.PropsDatabase?.GetPropsById(prefabId);
        if (propsConfig == null) {
            Debug.LogWarning($"[ServerPropManager] PropsConfig id={prefabId} not found in DatabaseManager — spawn aborted");
            return -1;
        }
        GameObject prefab = propsConfig.GetPrefab()?.gameObject;
        if (prefab == null) {
            Debug.LogWarning($"[ServerPropManager] PropsConfig id={prefabId} has no prefab assigned");
            return -1;
        }

        int propId = _nextAutoId++;

        GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
        instance.name = $"Prop{prefabId}#{propId}";

        PropIdentity identity = instance.GetComponent<PropIdentity>();
        if (identity == null) {
            Debug.LogError($"[ServerPropManager] Prefab '{prefabId}' is missing PropIdentity component");
            UnityEngine.Object.Destroy(instance);
            return -1;
        }
        identity.Assign(propId, roomId);

        ServerPropSource source = instance.GetComponent<ServerPropSource>();
        PropType         type    = source != null ? source.Type    : PropType.Generic;
        byte[]           payload = initialPayloadOverride
                                   ?? (source != null ? source.GetInitialState() : Array.Empty<byte>());

        RegisterInternal(roomId, propId, prefabId, position, rotation, type, payload, isScene: false);
        _spawnedGOs[propId] = instance;

        BroadcastToRoom(roomId, BuildSpawnMessage(roomId, propId, prefabId, position, rotation, type, payload));
        return propId;
    }

    /// <summary>Returns the server-side GameObject for a runtime-spawned prop (or null).</summary>
    public GameObject GetSpawnedGameObject(int propId) =>
        _spawnedGOs.TryGetValue(propId, out var go) ? go : null;

    // ── State update ──────────────────────────────────────────────────────────

    public void UpdatePropState(string roomId, int propId, byte[] payload) {
        if (!TryGetState(roomId, propId, out var state)) {
            Debug.LogWarning($"[ServerPropManager] Prop {propId} not found in room '{roomId}'");
            return;
        }
        state.Payload = payload;
        BroadcastToRoom(roomId, new S2C_PropUpdate {
            PropId  = propId,
            RoomId  = roomId,
            Type    = state.Type,
            Payload = payload
        });
    }

    /// <summary>
    /// Updates the position/rotation of a runtime-spawned prop and broadcasts to the room.
    /// Scene props cannot be moved — their transform is part of the scene asset.
    /// </summary>
    public void UpdatePropTransform(string roomId, int propId, Vector3 position, Quaternion rotation) {
        if (!TryGetState(roomId, propId, out var state)) {
            Debug.LogWarning($"[ServerPropManager] Prop {propId} not found in room '{roomId}'");
            return;
        }
        if (state.IsScene) {
            Debug.LogWarning($"[ServerPropManager] Cannot move scene prop {propId} in '{roomId}'");
            return;
        }
        state.Position = position;
        state.Rotation = rotation;

        if (_spawnedGOs.TryGetValue(propId, out var go) && go != null) {
            go.transform.position = position;
            go.transform.rotation = rotation;
        }

        BroadcastToRoom(roomId, new S2C_PropTransform {
            PropId   = propId,
            RoomId   = roomId,
            Position = position,
            Rotation = rotation
        });
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    public void RemoveProp(string roomId, int propId) {
        if (!_rooms.TryGetValue(roomId, out var room) || !room.TryGetValue(propId, out var state)) return;

        room.Remove(propId);
        if (_spawnedGOs.TryGetValue(propId, out var go)) {
            if (go != null) UnityEngine.Object.Destroy(go);
            _spawnedGOs.Remove(propId);
        }
        BroadcastToRoom(roomId, new S2C_PropRemove { PropId = propId, RoomId = roomId });
    }

    public void ClearRoom(string roomId) {
        if (!_rooms.TryGetValue(roomId, out var room)) return;
        foreach (int id in new List<int>(room.Keys)) {
            BroadcastToRoom(roomId, new S2C_PropRemove { PropId = id, RoomId = roomId });
            if (_spawnedGOs.TryGetValue(id, out var go)) {
                if (go != null) UnityEngine.Object.Destroy(go);
                _spawnedGOs.Remove(id);
            }
        }
        _rooms.Remove(roomId);
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the full current state of a room to one connection (room entry / late join).
    /// Scene props → S2C_PropUpdate (the GO already exists client-side).
    /// Spawned props → S2C_PropSpawn (the client must instantiate).
    /// </summary>
    public void SendRoomSnapshot(NetworkConnectionToClient conn, string roomId) {
        if (!_rooms.TryGetValue(roomId, out var room)) {
            conn.Send(new S2C_RoomSnapshot { RoomId = roomId, PropCount = 0 });
            return;
        }

        conn.Send(new S2C_RoomSnapshot { RoomId = roomId, PropCount = room.Count });

        foreach (var kv in room) {
            ServerPropState s = kv.Value;
            if (s.IsScene) {
                conn.Send(new S2C_PropUpdate {
                    PropId  = s.PropId,
                    RoomId  = s.RoomId,
                    Type    = s.Type,
                    Payload = s.Payload
                });
            } else {
                conn.Send(BuildSpawnMessage(s.RoomId, s.PropId, s.PrefabId, s.Position, s.Rotation, s.Type, s.Payload));
            }
        }
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public bool TryGetPropState(string roomId, int propId, out ServerPropState state) =>
        TryGetState(roomId, propId, out state);

    /// <summary>Returns a snapshot of all prop states in a room (safe to iterate while mutating).</summary>
    public IReadOnlyList<ServerPropState> GetRoomStates(string roomId) {
        if (!_rooms.TryGetValue(roomId, out var room))
            return System.Array.Empty<ServerPropState>();
        return new System.Collections.Generic.List<ServerPropState>(room.Values);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void RegisterInternal(
        string roomId, int propId, int prefabId,
        Vector3 position, Quaternion rotation, PropType type, byte[] payload, bool isScene
    ) {
        if (!_rooms.TryGetValue(roomId, out var room)) {
            room = new Dictionary<int, ServerPropState>();
            _rooms[roomId] = room;
        }
        room[propId] = new ServerPropState {
            PropId   = propId,
            PrefabId = prefabId,
            RoomId   = roomId,
            Position = position,
            Rotation = rotation,
            Type     = type,
            Payload  = payload ?? Array.Empty<byte>(),
            IsScene  = isScene
        };
    }

    private bool TryGetState(string roomId, int propId, out ServerPropState state) {
        state = null;
        return _rooms.TryGetValue(roomId, out var room) && room.TryGetValue(propId, out state);
    }

    private static S2C_PropSpawn BuildSpawnMessage(
        string roomId, int propId, int prefabId,
        Vector3 position, Quaternion rotation, PropType type, byte[] payload
    ) => new S2C_PropSpawn {
        PropId   = propId,
        PrefabId = prefabId,
        RoomId   = roomId,
        Position = position,
        Rotation = rotation,
        Type     = type,
        Payload  = payload ?? Array.Empty<byte>()
    };

    private static void BroadcastToRoom<T>(string roomId, T message) where T : struct, NetworkMessage {
        foreach (var conn in PlayerRoomTracker.Instance.GetConnectionsInRoom(roomId)) {
            conn.Send(message);
        }
    }
}
