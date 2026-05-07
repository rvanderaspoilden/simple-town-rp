using Mirror;
using UnityEngine;

public struct TeleportMessage : NetworkMessage {
    public Vector3 destination;
    /// <summary>
    /// If non-empty, the client calls ClientPropManager.EnterRoom(NewRoomId) after teleporting.
    /// Used to keep server-side PlayerRoomTracker in sync with the player's physical location.
    /// </summary>
    public string NewRoomId;
}