using System;
using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using UnityEngine;

/// <summary>
/// Manager autoritaire des NPC côté serveur.
///
/// • Plain C# singleton (pas de MonoBehaviour, pas de NetworkBehaviour).
/// • Mirror est utilisé UNIQUEMENT comme transport — pas de NetworkIdentity,
///   pas de NetworkTransform, pas de SyncVar.
/// • Broadcast scoped par room via PlayerRoomTracker (cohérent avec
///   ServerPropManager).
///
/// Lifecycle :
///   - Register(roomId, position, rotation)  → assigne npcId, broadcast Spawn
///   - PushTransform(npcId, position, rotation, velocity, animState)
///         → appelé par NpcAIController dans son Update
///         → throttle interne : on agrège, le tick configurable décide quand envoyer
///   - Unregister(npcId) → broadcast Destroy
///
/// La cadence d'envoi (UpdatesPerSecond) est configurable. À chaque tick,
/// les NPC qui ont franchi un seuil de déplacement / rotation OU dont l'état
/// d'animation a changé sont broadcastés.
/// </summary>
public class NpcServerManager {
    private static NpcServerManager _instance;
    public static NpcServerManager Instance => _instance ??= new NpcServerManager();

    // ── Configuration réseau ──────────────────────────────────────────────────
    /// <summary>Fréquence d'envoi des updates (Hz).</summary>
    public float UpdatesPerSecond = 10f;

    /// <summary>Distance minimale (mètres) avant de renvoyer une position.</summary>
    public float PositionThreshold = 0.05f;

    /// <summary>Angle minimal (degrés) avant de renvoyer une rotation.</summary>
    public float RotationThresholdDeg = 2f;

    /// <summary>Si false, la rotation n'est jamais incluse dans les deltas.</summary>
    public bool SyncRotation = true;

    // ── Stockage ──────────────────────────────────────────────────────────────
    private readonly Dictionary<int, NpcServerState>          _npcs        = new Dictionary<int, NpcServerState>();
    private readonly Dictionary<string, HashSet<int>>         _byRoom      = new Dictionary<string, HashSet<int>>();

    private int   _nextId       = 1;
    private float _accumulator  = 0f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Reset() {
        int count = _npcs.Count;
        _npcs.Clear();
        _byRoom.Clear();
        _nextId = 1;
        _accumulator = 0f;
        GameLogger.Network.Info("NpcServerManagerReset {Count}", count);
    }

    /// <summary>
    /// Enregistre un nouveau NPC dans une room et broadcast son spawn aux clients
    /// déjà présents dans cette room. Retourne le npcId attribué.
    /// </summary>
    public int Register(string roomId, Vector3 position, Quaternion rotation,
                         string styleJson = null, NpcIdentity identity = default,
                         string configId = null) {
        if (!NetworkServer.active) {
            GameLogger.Network.Warning("NpcRegisterNotServer {ConfigId} {RoomId}", configId, roomId);
            return -1;
        }
        if (string.IsNullOrEmpty(roomId)) {
            GameLogger.Network.Warning("NpcRegisterInvalidArgs {ConfigId} {RoomId}", configId, roomId);
            return -1;
        }

        int id = _nextId++;
        var state = new NpcServerState {
            NpcId          = id,
            RoomId         = roomId,
            StyleJson      = styleJson ?? string.Empty,
            Identity       = identity,
            ConfigId       = configId ?? string.Empty,
            Position       = position,
            Rotation       = rotation,
            Velocity       = Vector3.zero,
            State          = NpcStateType.Idle,

            LastSentPosition = position,
            LastSentRotation = rotation,
            LastSentState    = NpcStateType.Idle,
            EverSent         = false
        };
        _npcs[id] = state;

        if (!_byRoom.TryGetValue(roomId, out var set)) {
            set = new HashSet<int>();
            _byRoom[roomId] = set;
        }
        set.Add(id);

        BroadcastToRoom(roomId, new S2C_SpawnNpc {
            NpcId     = id,
            RoomId    = roomId,
            Position  = position,
            Rotation  = rotation,
            StyleJson = state.StyleJson,
            FirstName = identity.FirstName ?? string.Empty,
            LastName  = identity.LastName  ?? string.Empty,
            Mood      = (byte)identity.Mood,
            ConfigId  = state.ConfigId
        });

        GameLogger.Network.Info("NpcRegistered {NpcId} {ConfigId} {RoomId} {Position}",
            id, state.ConfigId, roomId, position);
        return id;
    }

    /// <summary>Met à jour le state pur (pas de broadcast immédiat — le tick décide).</summary>
    public void PushTransform(int npcId, Vector3 position, Quaternion rotation,
                              Vector3 velocity, NpcStateType stateType) {
        if (!_npcs.TryGetValue(npcId, out var s)) return;
        s.Position = position;
        s.Rotation = rotation;
        s.Velocity = velocity;
        s.State    = stateType;
    }

    /// <summary>
    /// Notifie un changement de state logique. Force un broadcast immédiat (sans
    /// attendre le tick) pour que les clients voient la transition au plus tôt
    /// — critique pour les states comme Sitting où l'animation doit changer
    /// précisément au moment où la position se fixe.
    /// </summary>
    public void NotifyStateChanged(int npcId, NpcStateType newState) {
        if (!_npcs.TryGetValue(npcId, out var s)) return;
        if (s.State == newState && s.EverSent) return;

        NpcStateType previous = s.State;
        s.State = newState;

        BroadcastToRoom(s.RoomId, new S2C_UpdateNpcTransform {
            NpcId    = s.NpcId,
            RoomId   = s.RoomId,
            Position = s.Position,
            Rotation = s.Rotation,
            Velocity = s.Velocity,
            State    = newState
        });

        s.LastSentPosition = s.Position;
        s.LastSentRotation = s.Rotation;
        s.LastSentState    = newState;
        s.EverSent         = true;

        GameLogger.Network.Info("NpcStateChanged {NpcId} {From} {To} {RoomId}",
            npcId, previous, newState, s.RoomId);
    }

    /// <summary>Diffuse le renversement (ragdoll) du NPC à sa room — effondrement sur place, sans
    /// projection. L'état persistant « renversé » est porté séparément par les snapshots
    /// (State = KnockedDown).</summary>
    public void Knockdown(int npcId) {
        if (!_npcs.TryGetValue(npcId, out var s)) return;
        BroadcastToRoom(s.RoomId, new S2C_NpcKnockdown {
            NpcId   = npcId,
            RoomId  = s.RoomId
        });
    }

    public void Unregister(int npcId) {
        if (!_npcs.TryGetValue(npcId, out var s)) return;
        _npcs.Remove(npcId);
        if (_byRoom.TryGetValue(s.RoomId, out var set)) {
            set.Remove(npcId);
            if (set.Count == 0) _byRoom.Remove(s.RoomId);
        }
        BroadcastToRoom(s.RoomId, new S2C_DestroyNpc { NpcId = npcId, RoomId = s.RoomId });
        GameLogger.Network.Info("NpcUnregistered {NpcId} {RoomId}", npcId, s.RoomId);
    }

    // ── Tick (appelé par NpcSystemBootstrap depuis MonoBehaviour Update) ──────

    public void Tick(float deltaTime) {
        if (UpdatesPerSecond <= 0f) return;
        _accumulator += deltaTime;
        float interval = 1f / UpdatesPerSecond;
        if (_accumulator < interval) return;
        _accumulator = 0f;

        FlushUpdates();
    }

    /// <summary>Broadcast immédiat des deltas qui dépassent les seuils.</summary>
    private void FlushUpdates() {
        float posThreshSq = PositionThreshold * PositionThreshold;

        foreach (var kv in _npcs) {
            NpcServerState s = kv.Value;

            bool posChanged   = !s.EverSent || (s.Position - s.LastSentPosition).sqrMagnitude >= posThreshSq;
            bool rotChanged   = SyncRotation && (!s.EverSent ||
                                Quaternion.Angle(s.Rotation, s.LastSentRotation) >= RotationThresholdDeg);
            bool stateChanged = !s.EverSent || s.State != s.LastSentState;

            if (!posChanged && !rotChanged && !stateChanged) continue;

            BroadcastToRoom(s.RoomId, new S2C_UpdateNpcTransform {
                NpcId    = s.NpcId,
                RoomId   = s.RoomId,
                Position = s.Position,
                Rotation = SyncRotation ? s.Rotation : s.LastSentRotation,
                Velocity = s.Velocity,
                State    = s.State
            });

            s.LastSentPosition = s.Position;
            if (SyncRotation) s.LastSentRotation = s.Rotation;
            s.LastSentState    = s.State;
            s.EverSent         = true;
        }
    }

    // ── Snapshot (envoi à un client qui entre dans la room) ───────────────────

    public void SendRoomSnapshot(NetworkConnectionToClient conn, string roomId) {
        if (!_byRoom.TryGetValue(roomId, out var ids)) return;

        int sent = 0;
        foreach (int id in ids) {
            if (!_npcs.TryGetValue(id, out var s)) continue;

            conn.Send(new S2C_SpawnNpc {
                NpcId     = s.NpcId,
                RoomId    = s.RoomId,
                Position  = s.Position,
                Rotation  = s.Rotation,
                StyleJson = s.StyleJson,
                FirstName = s.Identity.FirstName ?? string.Empty,
                LastName  = s.Identity.LastName  ?? string.Empty,
                Mood      = (byte)s.Identity.Mood,
                ConfigId  = s.ConfigId ?? string.Empty
            });
            // Envoie immédiatement un transform pour amorcer l'interpolation
            // avec velocity et animationState courants.
            conn.Send(new S2C_UpdateNpcTransform {
                NpcId    = s.NpcId,
                RoomId   = s.RoomId,
                Position = s.Position,
                Rotation = s.Rotation,
                Velocity = s.Velocity,
                State    = s.State
            });
            sent++;
        }
        GameLogger.Network.Debug("NpcSnapshotSent {ConnectionId} {RoomId} {Count}",
            conn.connectionId, roomId, sent);
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public IReadOnlyCollection<int> GetNpcIdsInRoom(string roomId) =>
        _byRoom.TryGetValue(roomId, out var set)
            ? (IReadOnlyCollection<int>)set
            : Array.Empty<int>();

    // ── Internal ──────────────────────────────────────────────────────────────

    private static void BroadcastToRoom<T>(string roomId, T message) where T : struct, NetworkMessage {
        var conns = PlayerRoomTracker.Instance.GetConnectionsInRoom(roomId);
        foreach (var conn in conns) {
            conn.Send(message);
        }
    }
}
