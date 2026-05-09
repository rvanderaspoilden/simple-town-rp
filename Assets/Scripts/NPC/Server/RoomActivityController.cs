using System;
using System.Collections.Generic;
using Mirror;
using Sim.Logging;

/// <summary>
/// Tracks server-side room activity based on player presence.
///
/// A room is considered <b>active</b> while at least one player is connected
/// to it. When a room transitions from empty to populated (or back), events
/// are fired so subsystems (e.g. NPC AI, pathfinding) can suspend/resume.
///
/// Plain C# singleton — no MonoBehaviour. Driven by <see cref="PlayerRoomTracker"/>
/// events wired by <see cref="NpcSystemBootstrap"/>.
///
/// Design notes
///   • Rooms are lazily tracked: we only create an entry the first time a
///     player enters the room. An unknown room is implicitly inactive.
///   • NPCs are never despawned when a room becomes inactive — they stay in
///     memory (for future pooling consistency). We just stop ticking them.
///     See <see cref="NpcAIController.Update"/>.
/// </summary>
public class RoomActivityController {
    private static RoomActivityController _instance;
    public static RoomActivityController Instance => _instance ??= new RoomActivityController();

    // roomId → number of players currently in the room
    private readonly Dictionary<string, int> _playerCounts = new Dictionary<string, int>();

    /// <summary>Fired when a room transitions from 0 → ≥1 players.</summary>
    public static event Action<string> OnRoomActivated;

    /// <summary>Fired when a room transitions from ≥1 → 0 players.</summary>
    public static event Action<string> OnRoomDeactivated;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Reset() {
        _playerCounts.Clear();
    }

    // ── Event sinks (hooked by NpcSystemBootstrap) ────────────────────────────

    public void HandlePlayerEnterRoom(NetworkConnectionToClient conn, string roomId) {
        if (string.IsNullOrEmpty(roomId)) return;

        _playerCounts.TryGetValue(roomId, out int count);
        int next = count + 1;
        _playerCounts[roomId] = next;

        if (count == 0 && next == 1) {
            GameLogger.Network.Info("NpcRoomResumed {RoomId}", roomId);
            OnRoomActivated?.Invoke(roomId);
        }
    }

    public void HandlePlayerLeaveRoom(NetworkConnectionToClient conn, string roomId) {
        if (string.IsNullOrEmpty(roomId)) return;
        if (!_playerCounts.TryGetValue(roomId, out int count) || count <= 0) return;

        int next = count - 1;
        if (next <= 0) {
            _playerCounts.Remove(roomId);
            GameLogger.Network.Info("NpcRoomSleeping {RoomId}", roomId);
            OnRoomDeactivated?.Invoke(roomId);
        }
        else {
            _playerCounts[roomId] = next;
        }
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>True if at least one player is currently connected to the room.</summary>
    public bool IsRoomActive(string roomId) {
        if (string.IsNullOrEmpty(roomId)) return false;
        return _playerCounts.TryGetValue(roomId, out int count) && count > 0;
    }

    public int GetPlayerCount(string roomId) =>
        _playerCounts.TryGetValue(roomId, out int c) ? c : 0;
}
