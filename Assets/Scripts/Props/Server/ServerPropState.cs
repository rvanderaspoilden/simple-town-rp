using UnityEngine;

/// <summary>
/// Mutable server-side record for one prop instance.
/// Payload is the serialized typed state (DoorState, SeatState, …).
///
/// IsScene = true  → prop already exists in the City scene on every client.
///                   Snapshot sends only S2C_PropUpdate (state only).
/// IsScene = false → prop was spawned at runtime (apartment furniture, etc.).
///                   Snapshot sends S2C_PropSpawn (full instantiation info).
/// </summary>
public class ServerPropState {
    public int        PropId;
    public string     PrefabId;
    public string     RoomId;
    public Vector3    Position;
    public Quaternion Rotation;
    public PropType   Type;
    public byte[]     Payload;
    public bool       IsScene;
}
