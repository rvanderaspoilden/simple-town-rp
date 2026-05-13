using System;
using System.Collections.Generic;
using Mirror;
using Sim.Logging;
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

    // Bridge runtime int propId → persistent UUID + last-known version in the
    // new `props` table. Set by callers (build flow, place state load) right
    // after SpawnProp. Lets later runtime events (move, lock, paint, …) PATCH
    // the correct DB row without needing to plumb the UUID through every state
    // update. Version is required by the backend's optimistic locking; we keep
    // it current after each successful PATCH.
    public class PropDbBridge {
        public string Uuid;
        public int    Version;
    }
    private readonly Dictionary<int, PropDbBridge> _propBridges = new Dictionary<int, PropDbBridge>();

    private readonly Dictionary<string, byte[]> _roomStates = new Dictionary<string, byte[]>();

    // Per-apartment state, grouped by room id. Each apartment in a hall room stores its
    // own payload under a unique key (typically the ApartmentKey). The snapshot resends
    // every entry so newcomers see all apartment states in the floor they enter.
    private readonly Dictionary<string, Dictionary<string, byte[]>> _apartmentStatesByRoom
        = new Dictionary<string, Dictionary<string, byte[]>>();

    private int _nextAutoId = 10000; // runtime IDs start high to avoid collisions with scene IDs (1..9999)

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Reset() {
        int spawnedCount = _spawnedGOs.Count;
        foreach (var go in _spawnedGOs.Values) {
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        _spawnedGOs.Clear();
        _rooms.Clear();
        _roomStates.Clear();
        _propBridges.Clear();
        _nextAutoId = 10000;
        GameLogger.Network.Info("ServerPropManagerReset {DestroyedProps}", spawnedCount);
    }

    // ── int propId ↔ persistent UUID bridge ───────────────────────────────────

    /// <summary>Associate a runtime int propId with its UUID + initial version.</summary>
    public void AssociateUuid(int propId, string propUuid, int version = 1) {
        if (propId <= 0 || string.IsNullOrEmpty(propUuid)) return;
        _propBridges[propId] = new PropDbBridge { Uuid = propUuid, Version = version };
    }

    /// <summary>Returns the persistent UUID for a runtime propId, or null if not bridged.</summary>
    public string GetUuid(int propId) =>
        _propBridges.TryGetValue(propId, out PropDbBridge b) ? b.Uuid : null;

    /// <summary>Returns the full bridge (UUID + version), or null if not bridged.</summary>
    public PropDbBridge GetBridge(int propId) =>
        _propBridges.TryGetValue(propId, out PropDbBridge b) ? b : null;

    /// <summary>Bumps the cached version after a successful PATCH /props/:id.</summary>
    public void UpdateVersion(int propId, int newVersion) {
        if (_propBridges.TryGetValue(propId, out PropDbBridge b)) b.Version = newVersion;
    }

    /// <summary>Drops the bridge for a prop (called after DELETE /props/:id).</summary>
    public void ClearBridge(int propId) =>
        _propBridges.Remove(propId);

    /// <summary>Reverse lookup: int propId for a given UUID, or -1 if not bridged.</summary>
    public int FindPropIdByUuid(string uuid) {
        if (string.IsNullOrEmpty(uuid)) return -1;
        foreach (var kv in _propBridges) {
            if (kv.Value != null && kv.Value.Uuid == uuid) return kv.Key;
        }
        return -1;
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
            GameLogger.Network.Warning("ScenePropNoId {ObjectName}", source.gameObject.name);
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
        
        GameLogger.Network.Debug("ScenePropRegistered {PropId} {RoomId} {PrefabId}", source.PropId, source.RoomId, prefabId);
    }

    // ── Runtime spawning ──────────────────────────────────────────────────────

    /// <summary>
    /// Instantiates a prop prefab server-side, assigns a propId via PropIdentity,
    /// records its state, and broadcasts S2C_PropSpawn to current room members.
    /// Returns the assigned propId (or -1 on failure).
    ///
    /// <paramref name="initialPayloadOverride"/> entirely replaces the payload generated by
    /// the ServerPropSource (used for PaintBucket where we want to inject paint config + color).
    /// <paramref name="headerOverride"/> only rewrites the first <c>PropStateHeader.ByteSize</c>
    /// bytes (isBuilt + presetId) of the resulting payload — letting the source generate the
    /// type-specific body (seat slots, delivery count, ...) while the build flow controls
    /// the build/preset state.
    /// </summary>
    /// <summary>
    /// Optional persistent UUID + version to bridge with the new `props` table.
    /// Provide these when loading from /places/:id/state — the resulting runtime
    /// int propId is auto-associated with the UUID, so subsequent state changes
    /// PATCH the right DB row without callers having to track the mapping.
    /// </summary>
    public int SpawnProp(
        string             roomId,
        int                prefabId,
        Vector3            position,
        Quaternion         rotation,
        byte[]             initialPayloadOverride = null,
        PropStateHeader?   headerOverride         = null,
        string             propUuid               = null,
        int                propVersion            = 1
    ) {
        if (!NetworkServer.active) {
            GameLogger.Network.Warning("SpawnPropNotServer {PrefabId} {RoomId}", prefabId, roomId);
            return -1;
        }

        var propsConfig = Sim.DatabaseManager.GetPropsById(prefabId);
        if (propsConfig == null) {
            GameLogger.Network.Warning("SpawnPropConfigNotFound {PrefabId} {RoomId}", prefabId, roomId);
            return -1;
        }
        GameObject prefab = propsConfig.GetPrefab()?.gameObject;
        if (prefab == null) {
            GameLogger.Network.Warning("SpawnPropPrefabNull {PrefabId} {RoomId}", prefabId, roomId);
            return -1;
        }

        int propId = _nextAutoId++;

        GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
        instance.name = $"Prop{prefabId}#{propId}";

        PropIdentity identity = instance.GetComponent<PropIdentity>();
        if (identity == null) {
            GameLogger.Network.Error(null, "SpawnPropNoIdentity {PrefabId} {PropId}", prefabId, propId);
            UnityEngine.Object.Destroy(instance);
            return -1;
        }
        identity.Assign(propId, roomId);

        ServerPropSource source = instance.GetComponent<ServerPropSource>();
        PropType         type    = source != null ? source.Type    : PropType.Generic;
        byte[]           payload = initialPayloadOverride
                                   ?? (source != null ? source.GetInitialState() : Array.Empty<byte>());

        if (headerOverride.HasValue) {
            // Ensure payload is large enough to hold the header; create a minimal one if the source
            // didn't provide one (e.g. prop has no ServerPropSource component).
            if (payload == null || payload.Length < PropStateHeader.ByteSize) {
                byte[] extended = new byte[PropStateHeader.ByteSize + (payload?.Length ?? 0)];
                payload?.CopyTo(extended, PropStateHeader.ByteSize);
                payload = extended;
            } else {
                payload = (byte[])payload.Clone();
            }
            headerOverride.Value.WriteTo(payload, 0);
            GameLogger.Network.Debug("PropPayloadHeaderOverride {PropId} {IsBuilt} {PresetId}",
                propId, headerOverride.Value.IsBuilt, headerOverride.Value.PresetId);
        }

        RegisterInternal(roomId, propId, prefabId, position, rotation, type, payload, isScene: false);
        _spawnedGOs[propId] = instance;

        // Auto-bridge with the persistent UUID when the caller knows it (loading
        // from /places/:id/state, or after a POST /props at buy/build).
        if (!string.IsNullOrEmpty(propUuid)) {
            AssociateUuid(propId, propUuid, propVersion);
        }

        BroadcastToRoom(roomId, BuildSpawnMessage(roomId, propId, prefabId, position, rotation, type, payload));

        GameLogger.Network.Info("PropSpawned {PropId} {PrefabId} {RoomId} {Position} {PresetId}",
            propId, prefabId, roomId, position,
            headerOverride.HasValue ? headerOverride.Value.PresetId : PropStateHeader.ReadFrom(payload).PresetId);
        return propId;
    }

    /// <summary>Returns the server-side GameObject for a runtime-spawned prop (or null).</summary>
    public GameObject GetSpawnedGameObject(int propId) =>
        _spawnedGOs.TryGetValue(propId, out var go) ? go : null;

    // ── State update ──────────────────────────────────────────────────────────

    public void UpdatePropState(string roomId, int propId, byte[] payload) {
        if (!TryGetState(roomId, propId, out var state)) {
            GameLogger.Network.Warning("UpdatePropStateNotFound {PropId} {RoomId}", propId, roomId);
            return;
        }
        state.Payload = payload;
        BroadcastToRoom(roomId, new S2C_PropUpdate {
            PropId  = propId,
            RoomId  = roomId,
            Type    = state.Type,
            Payload = payload
        });
        GameLogger.Network.Debug("PropStateUpdated {PropId} {RoomId} {PayloadSize}", propId, roomId, payload?.Length ?? 0);
    }

    /// <summary>
    /// Updates the position/rotation of a runtime-spawned prop and broadcasts to the room.
    /// Scene props cannot be moved — their transform is part of the scene asset.
    /// </summary>
    public void UpdatePropTransform(string roomId, int propId, Vector3 position, Quaternion rotation) {
        if (!TryGetState(roomId, propId, out var state)) {
            GameLogger.Network.Warning("UpdateTransformNotFound {PropId} {RoomId}", propId, roomId);
            return;
        }
        if (state.IsScene) {
            GameLogger.Network.Warning("UpdateTransformSceneProp {PropId} {RoomId}", propId, roomId);
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
        GameLogger.Network.Debug("PropTransformUpdated {PropId} {RoomId} {Position}", propId, roomId, position);
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    public void RemoveProp(string roomId, int propId) {
        if (!_rooms.TryGetValue(roomId, out var room) || !room.TryGetValue(propId, out var state)) {
            GameLogger.Network.Warning("RemovePropNotFound {PropId} {RoomId}", propId, roomId);
            return;
        }

        room.Remove(propId);
        if (_spawnedGOs.TryGetValue(propId, out var go)) {
            if (go != null) {
                UnityEngine.Object.Destroy(go);
                GameLogger.Network.Info("PropRemoved {PropId} {RoomId}", propId, roomId);
            }
            _spawnedGOs.Remove(propId);
        }
        BroadcastToRoom(roomId, new S2C_PropRemove { PropId = propId, RoomId = roomId });
    }

    public void ClearRoom(string roomId) {
        if (!_rooms.TryGetValue(roomId, out var room)) return;
        int count = room.Count;
        foreach (int id in new List<int>(room.Keys)) {
            BroadcastToRoom(roomId, new S2C_PropRemove { PropId = id, RoomId = roomId });
            if (_spawnedGOs.TryGetValue(id, out var go)) {
                if (go != null) UnityEngine.Object.Destroy(go);
                _spawnedGOs.Remove(id);
            }
        }
        _rooms.Remove(roomId);
        GameLogger.Network.Info("RoomCleared {RoomId} {PropCount}", roomId, count);
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
            if (_roomStates.TryGetValue(roomId, out var statePayload))
                conn.Send(new S2C_RoomState { RoomId = roomId, Payload = statePayload });
            GameLogger.Network.Debug("RoomSnapshotEmpty {ConnectionId} {RoomId}", conn.connectionId, roomId);
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

        if (_roomStates.TryGetValue(roomId, out var roomStatePayload))
            conn.Send(new S2C_RoomState { RoomId = roomId, Payload = roomStatePayload });

        if (_apartmentStatesByRoom.TryGetValue(roomId, out var byApt)) {
            foreach (var kv in byApt)
                conn.Send(new S2C_RoomState { RoomId = roomId, Payload = kv.Value });
        }
        
        GameLogger.Network.Debug("RoomSnapshotSent {ConnectionId} {RoomId} {PropCount}", conn.connectionId, roomId, room.Count);
    }

    // ── RoomState (Phase 2) ───────────────────────────────────────────────────

    public void SetRoomState(string roomId, byte[] payload) {
        _roomStates[roomId] = payload;
        BroadcastToRoom(roomId, new S2C_RoomState { RoomId = roomId, Payload = payload });
        GameLogger.Network.Debug("RoomStateSet {RoomId} {PayloadSize}", roomId, payload?.Length ?? 0);
    }

    /// <summary>
    /// Stores a per-apartment state payload (multiple apartments can coexist in the same
    /// hall room) and broadcasts an S2C_RoomState to all connections in that room.
    /// On EnterRoom, every stored entry for the room is resent so late joiners see them all.
    /// </summary>
    public void SetApartmentState(string roomId, string apartmentKey, byte[] payload) {
        if (!_apartmentStatesByRoom.TryGetValue(roomId, out var byApt)) {
            byApt = new Dictionary<string, byte[]>();
            _apartmentStatesByRoom[roomId] = byApt;
        }
        byApt[apartmentKey] = payload;
        BroadcastToRoom(roomId, new S2C_RoomState { RoomId = roomId, Payload = payload });
        GameLogger.Network.Debug("ApartmentStateSet {RoomId} {ApartmentKey} {PayloadSize}", roomId, apartmentKey, payload?.Length ?? 0);
    }

    public void RemoveApartmentState(string roomId, string apartmentKey) {
        if (_apartmentStatesByRoom.TryGetValue(roomId, out var byApt))
            byApt.Remove(apartmentKey);
    }

    public bool TryGetRoomState(string roomId, out byte[] payload) =>
        _roomStates.TryGetValue(roomId, out payload);

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
