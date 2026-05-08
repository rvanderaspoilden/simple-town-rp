// All item network messages live in the global namespace.
// Mirror's handler registration is reflection-based — mixing namespaces breaks matching.
using Mirror;
using UnityEngine;

/// <summary>Server → all clients in room: item spawned (or late-join snapshot entry).</summary>
public struct S2C_SpawnItem : NetworkMessage
{
    public int    EntityId;
    public string RoomId;
    public int    ItemConfigId;
    public Vector3    Position;
    public Quaternion Rotation;
    // Held state (populated for snapshot entries of already-held items)
    public bool   IsHeld;
    public uint   HolderNetId;
    public HandType HolderHand;
    public Vector3    LocalPosition;
    public Quaternion LocalRotation;
}

/// <summary>Server → all clients in room: item removed from world.</summary>
public struct S2C_DestroyItem : NetworkMessage
{
    public int    EntityId;
    public string RoomId;
}

/// <summary>Client → server: player wants to pick up an item.</summary>
public struct C2S_RequestPickupItem : NetworkMessage
{
    public int EntityId;
}

/// <summary>Server → requesting client only: result of a pickup attempt.</summary>
public struct S2C_PickupResult : NetworkMessage
{
    public bool   Success;
    public int    EntityId;
    public string ErrorMessage;
}

/// <summary>Server → all clients in room: item attached to a player's hand.</summary>
public struct S2C_ItemAttachedToHand : NetworkMessage
{
    public int    EntityId;
    public uint   PlayerNetId;
    public HandType HandType;
    public Vector3    LocalPosition;
    public Quaternion LocalRotation;
}

/// <summary>Server → all clients in room: item detached from a hand and dropped.</summary>
public struct S2C_ItemDetachedFromHand : NetworkMessage
{
    public int    EntityId;
    public Vector3    WorldPosition;
    public Quaternion WorldRotation;
}

/// <summary>Client → server: player wants to drop the item in the specified hand.</summary>
public struct C2S_RequestDropItem : NetworkMessage
{
    public HandType Hand;
}

/// <summary>Server → requesting client only: result of a drop attempt.</summary>
public struct S2C_DropResult : NetworkMessage
{
    public bool   Success;
    public HandType Hand;
    public string ErrorMessage;
}

/// <summary>Client → server: swap left ↔ right hand items.</summary>
public struct C2S_RequestSwapHands : NetworkMessage { }

/// <summary>Admin/debug: client requests a server-side item spawn at a position.</summary>
public struct C2S_AdminSpawnItem : NetworkMessage
{
    public int     ItemConfigId;
    public Vector3 Position;
}
