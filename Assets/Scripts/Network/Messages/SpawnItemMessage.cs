using Mirror;
using UnityEngine;

public struct SpawnItemMessage : NetworkMessage {
    public int itemId;
    public Vector3 position;
}