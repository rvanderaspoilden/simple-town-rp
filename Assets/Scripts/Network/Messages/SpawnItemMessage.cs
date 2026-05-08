// OBSOLETE — replaced by C2S_AdminSpawnItem + ServerItemManager.SpawnItem.
// Kept to avoid breaking serialized scene references. Safe to delete once confirmed unused.
using Mirror;
using UnityEngine;

[System.Obsolete("Use C2S_AdminSpawnItem instead. This message is no longer registered on the server.")]
public struct SpawnItemMessage : NetworkMessage
{
    public int itemId;
    public Vector3 position;
}
