using UnityEngine;

/// <summary>
/// Server-side source for Seat props.
/// Reads slot counts from the sibling SeatBehaviour to initialise the multi-slot SeatState.
/// Reads defaultPresetId from sibling PropBehaviourBase if present.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
[RequireComponent(typeof(SeatBehaviour))]
public class SeatPropSource : ServerPropSource {
    public override PropType Type => PropType.Seat;

    private SeatBehaviour _seatBehaviour;
    private PropBehaviourBase _propBehaviour;

    private void Awake() {
        _seatBehaviour = GetComponent<SeatBehaviour>();
        _propBehaviour = GetComponent<PropBehaviourBase>();
    }

    public override byte[] GetInitialState() {
        int seats   = _seatBehaviour != null ? _seatBehaviour.SeatSlotCount  : 0;
        int couches = _seatBehaviour != null ? _seatBehaviour.CouchSlotCount : 0;
        int presetId = _propBehaviour != null ? _propBehaviour.DefaultPresetId : -1;
        return new SeatState {
            Header         = new PropStateHeader { IsBuilt = true, PresetId = presetId },
            SeatOccupants  = new uint[seats],
            CouchOccupants = new uint[couches]
        }.Serialize();
    }
}
