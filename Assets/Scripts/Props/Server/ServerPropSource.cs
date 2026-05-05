using UnityEngine;

/// <summary>
/// Base class for the server-side logic of a prop. Lives on the SAME GameObject
/// as the client-side IPropBehaviour and the visual components — both server
/// and client share one prefab. Server-only code must be guarded by
/// NetworkServer.active inside the subclass (e.g. OnTriggerEnter).
///
/// PropId and RoomId are read from the sibling PropIdentity component
/// (hardcoded by the designer for scene props, assigned by ServerPropManager
/// for runtime-spawned props).
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public abstract class ServerPropSource : MonoBehaviour {
    private PropIdentity _identity;
    protected PropIdentity Identity => _identity != null ? _identity : (_identity = GetComponent<PropIdentity>());

    public int    PropId => Identity.PropId;
    public string RoomId => Identity.RoomId;

    /// <summary>Prop category used to route client-side deserialization.</summary>
    public abstract PropType Type { get; }

    /// <summary>Binary payload representing the prop's initial state.</summary>
    public abstract byte[] GetInitialState();
}
