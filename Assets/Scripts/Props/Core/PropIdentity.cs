using UnityEngine;

/// <summary>
/// Shared component placed on every prop prefab. Stores the prop's identity
/// (propId + roomId) consumed by both the server-side ServerPropSource and
/// the client-side IPropBehaviour on the same GameObject.
///
/// For city scene props, propId is set in the Inspector (hardcoded by the designer).
/// For apartment props, propId is assigned at runtime by ServerPropManager / ClientPropManager.
/// </summary>
public class PropIdentity : MonoBehaviour {
    [SerializeField] private int    propId;
    [SerializeField] private string roomId = "city";

    public int    PropId => propId;
    public string RoomId => roomId;

    /// <summary>
    /// Fires synchronously at the end of <see cref="Assign"/> so sibling components
    /// (e.g. SeatBehaviour's propId→instance registry) can pick up the new id —
    /// without this, runtime-spawned props miss their registration because their
    /// Awake/OnEnable run with propId=0, before ServerPropManager assigns it.
    /// </summary>
    public event System.Action<int, string> OnAssigned;

    /// <summary>Assigned by ServerPropManager / ClientPropManager for runtime-spawned props.</summary>
    public void Assign(int id, string room) {
        propId = id;
        roomId = room;
        OnAssigned?.Invoke(id, room);
    }
}
