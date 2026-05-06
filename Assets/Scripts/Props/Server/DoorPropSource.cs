using System.Collections.Generic;
using Mirror;
using Sim.Building;
using UnityEngine;

/// <summary>
/// Server-side trigger that drives door open/close state via ServerPropManager.
/// Place this component (with PropIdentity) on every door prefab — front and inner.
/// The lock state and door number are part of the DoorState payload, set by the
/// apartment via ServerPropManager.UpdatePropState.
/// </summary>
public class DoorPropSource : ServerPropSource {
    [Header("Default state")]
    [SerializeField] private DoorLockState defaultLockState = DoorLockState.UNLOCKED;

    public override PropType Type => PropType.Door;

    public override byte[] GetInitialState() =>
        new DoorState {
            Header     = PropStateHeader.Default,
            IsOpen     = false,
            LockState  = defaultLockState,
            DoorNumber = 0
        }.Serialize();

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
        if (!ServerPropManager.Instance.TryGetPropState(RoomId, PropId, out var state)) return;
        DoorState current = DoorState.Deserialize(state.Payload);
        bool shouldOpen = current.LockState == DoorLockState.UNLOCKED && _occupants.Count > 0;
        if (shouldOpen == current.IsOpen) return;

        current.IsOpen = shouldOpen;
        ServerPropManager.Instance.UpdatePropState(RoomId, PropId, current.Serialize());
    }

    private static bool IsPlayer(Collider c) =>
        c.gameObject.layer == LayerMask.NameToLayer("Player");
}
