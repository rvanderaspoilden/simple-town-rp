using UnityEngine;

/// <summary>
/// Lightweight identity component placed on every item prefab.
/// Replaces NetworkIdentity for item GameObjects.
/// </summary>
[DisallowMultipleComponent]
public class ItemIdentity : MonoBehaviour
{
    private int    _entityId;
    private string _roomId;

    public int    EntityId => _entityId;
    public string RoomId   => _roomId;

    public void Assign(int entityId, string roomId)
    {
        _entityId = entityId;
        _roomId   = roomId;
    }
}
