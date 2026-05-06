using UnityEngine;

/// <summary>
/// Server-side source for Seat props.
/// Reads slot counts from the sibling SeatBehaviour to initialise the multi-slot SeatState.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
[RequireComponent(typeof(SeatBehaviour))]
public class SeatPropSource : ServerPropSource {
    public override PropType Type => PropType.Seat;

    private SeatBehaviour _behaviour;

    private void Awake() {
        _behaviour = GetComponent<SeatBehaviour>();
    }

    public override byte[] GetInitialState() {
        int seats   = _behaviour != null ? _behaviour.SeatSlotCount  : 0;
        int couches = _behaviour != null ? _behaviour.CouchSlotCount : 0;
        return new SeatState {
            Header         = PropStateHeader.Default,
            SeatOccupants  = new uint[seats],
            CouchOccupants = new uint[couches]
        }.Serialize();
    }
}
