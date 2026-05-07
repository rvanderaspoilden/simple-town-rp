using Mirror;
using UnityEngine;

/// <summary>Sent by server to all clients when a new hall floor is instantiated.</summary>
public struct S2C_HallSpawn : NetworkMessage {
    public string  Street;
    public int     FloorNumber;
    public Vector3 Position;
}

/// <summary>Sent by server to all clients when a hall floor is destroyed.</summary>
public struct S2C_HallDespawn : NetworkMessage {
    public string Street;
    public int    FloorNumber;
}

/// <summary>
/// Sent by server to all clients once an apartment is fully generated.
/// The client instantiates the apartment prefab and calls ClientSetup.
/// </summary>
public struct S2C_ApartmentSpawn : NetworkMessage {
    public string     Street;
    public int        FloorNumber;
    public int        DoorNumber;
    public string     PresetName;   // empty when NOT_GENERATED
    public Vector3    Position;
    public Quaternion Rotation;
}
