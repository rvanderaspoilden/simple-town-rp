using System;
using System.Collections.Generic;
using Mirror;

/// <summary>
/// Tracks which room each connected player is in.
/// Used by ServerPropManager to scope broadcasts to room members only.
/// Plain C# singleton — no MonoBehaviour, no Unity lifecycle.
/// </summary>
public class PlayerRoomTracker {
    private static PlayerRoomTracker _instance;
    public static PlayerRoomTracker Instance => _instance ??= new PlayerRoomTracker();

    // conn → current roomId
    private readonly Dictionary<NetworkConnectionToClient, string> _connToRoom
        = new Dictionary<NetworkConnectionToClient, string>();

    // roomId → set of connections
    private readonly Dictionary<string, HashSet<NetworkConnectionToClient>> _roomToConns
        = new Dictionary<string, HashSet<NetworkConnectionToClient>>();

    /// <summary>Wipes all state. Call on server stop.</summary>
    public void Reset() {
        _connToRoom.Clear();
        _roomToConns.Clear();
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves a connection into a room.
    /// Automatically leaves the previous room if different.
    /// </summary>
    public void EnterRoom(NetworkConnectionToClient conn, string roomId) {
        if (string.IsNullOrEmpty(roomId)) return;

        if (_connToRoom.TryGetValue(conn, out string prev) && prev != roomId) {
            RemoveFromRoom(conn, prev);
        }

        _connToRoom[conn] = roomId;

        if (!_roomToConns.TryGetValue(roomId, out var set)) {
            set = new HashSet<NetworkConnectionToClient>();
            _roomToConns[roomId] = set;
        }
        set.Add(conn);
    }

    /// <summary>Removes a connection from its current room without entering another.</summary>
    public void LeaveRoom(NetworkConnectionToClient conn) {
        if (_connToRoom.TryGetValue(conn, out string roomId)) {
            RemoveFromRoom(conn, roomId);
        }
    }

    /// <summary>Full cleanup on disconnect.</summary>
    public void OnDisconnect(NetworkConnectionToClient conn) => LeaveRoom(conn);

    // ── Queries ───────────────────────────────────────────────────────────────

    public string GetRoom(NetworkConnectionToClient conn) =>
        _connToRoom.TryGetValue(conn, out string r) ? r : null;

    public IEnumerable<NetworkConnectionToClient> GetConnectionsInRoom(string roomId) =>
        _roomToConns.TryGetValue(roomId, out var set)
            ? (IEnumerable<NetworkConnectionToClient>)set
            : Array.Empty<NetworkConnectionToClient>();

    // ── Internal ──────────────────────────────────────────────────────────────

    private void RemoveFromRoom(NetworkConnectionToClient conn, string roomId) {
        _connToRoom.Remove(conn);
        if (_roomToConns.TryGetValue(roomId, out var set)) {
            set.Remove(conn);
            if (set.Count == 0) _roomToConns.Remove(roomId);
        }
    }
}
