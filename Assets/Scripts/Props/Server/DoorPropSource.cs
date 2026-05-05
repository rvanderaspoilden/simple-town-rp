using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Server-only trigger that drives door open/close state via ServerPropManager.
/// Attach to a trigger collider in the city scene (no renderer needed).
/// propId must be unique within the room and match the client prefab registered
/// in PropPrefabDatabase under the same key.
/// </summary>
public class DoorPropSource : ServerPropSource {
    public override PropType Type => PropType.Door;

    public override byte[] GetInitialState() =>
        new DoorState { IsOpen = false }.Serialize();

    private readonly List<Collider> _occupants = new List<Collider>();

    private void OnTriggerEnter(Collider other) {
        if (!NetworkServer.active) return;
        if (!IsPlayer(other)) return;
        if (!_occupants.Contains(other)) _occupants.Add(other);
        Sync();
    }

    private void OnTriggerExit(Collider other) {
        if (!NetworkServer.active) return;
        _occupants.RemoveAll(c => c == null || c == other);
        Sync();
    }

    private void Sync() {
        ServerPropManager.Instance.UpdatePropState(
            RoomId, PropId,
            new DoorState { IsOpen = _occupants.Count > 0 }.Serialize()
        );
    }

    private static bool IsPlayer(Collider c) =>
        c.gameObject.layer == LayerMask.NameToLayer("Player");
}
