using Mirror;
using UnityEngine;

public struct TeleportMessage : NetworkMessage {
    public Vector3 destination;
}