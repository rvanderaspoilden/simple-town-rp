using UnityEngine;

/// <summary>
/// Authoritative server-side record of a world item.
/// Plain data — no MonoBehaviour, no Mirror, no Unity lifecycle.
/// </summary>
public class ItemEntity
{
    public int    EntityId;
    public string RoomId;
    public int    ItemConfigId;
    public Vector3    Position;
    public Quaternion Rotation;

    // Hand state — zeroed when not held
    public uint     HolderNetId;      // 0 = not held
    public HandType HolderHand;
    public Vector3    LocalPosition;
    public Quaternion LocalRotation;

    public bool IsHeld => HolderNetId != 0;
}
