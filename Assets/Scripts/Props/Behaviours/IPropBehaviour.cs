/// <summary>
/// Implemented by client-side MonoBehaviours that visualize a prop's state.
/// PropId and RoomId are read directly from a sibling PropIdentity component.
/// ApplyState must be idempotent — it can be called multiple times with the
/// same payload and must produce the same visual result.
/// </summary>
public interface IPropBehaviour {
    /// <summary>
    /// Called every time the server sends a state update for this prop.
    /// Deserialize payload based on PropType and update visuals/logic.
    /// </summary>
    void ApplyState(PropType type, byte[] payload);
}
