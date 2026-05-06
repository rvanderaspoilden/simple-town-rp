using System.Linq;
using Interaction;
using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for Seat props (chairs, benches, couches, beds).
/// Handles any number of seat slots and couch slots on the same GameObject.
/// The server picks the first available slot; the client detects which slot
/// it was assigned by matching its netId in the incoming SeatState.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class SeatBehaviour : PropBehaviourBase, ISeatBehavior {
    [Header("Seat slots")]
    [SerializeField] private Transform[] seatTransforms;
    [SerializeField] private Transform[] couchTransforms;

    // Exposed so SeatPropSource can read slot counts without reflection
    public int SeatSlotCount  => seatTransforms?.Length ?? 0;
    public int CouchSlotCount => couchTransforms?.Length ?? 0;

    private SeatState _state;

    protected override void Awake() {
        base.Awake();
        _state = new SeatState {
            SeatOccupants  = new uint[SeatSlotCount],
            CouchOccupants = new uint[CouchSlotCount]
        };
    }

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        base.ApplyState(type, payload);

        SeatState incoming = SeatState.Deserialize(payload);
        uint localNetId = PlayerController.Local?.netId ?? 0;

        if (localNetId != 0) {
            for (int i = 0; i < incoming.SeatOccupants.Length; i++) {
                bool wasHere = _state.SeatOccupants != null
                            && i < _state.SeatOccupants.Length
                            && _state.SeatOccupants[i] == localNetId;
                if (incoming.SeatOccupants[i] == localNetId && !wasHere && i < seatTransforms.Length) {
                    PlayerController.Local.Sit(this, seatTransforms[i]);
                }
            }
            for (int i = 0; i < incoming.CouchOccupants.Length; i++) {
                bool wasHere = _state.CouchOccupants != null
                            && i < _state.CouchOccupants.Length
                            && _state.CouchOccupants[i] == localNetId;
                if (incoming.CouchOccupants[i] == localNetId && !wasHere && i < couchTransforms.Length) {
                    PlayerController.Local.Sleep(this, couchTransforms[i]);
                }
            }
        }

        _state = incoming;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public override bool IsInteractable() {
        if (!base.IsInteractable()) return false;
        uint localNetId = PlayerController.Local?.netId ?? 0;
        if (IsLocalPlayerOccupying(localNetId)) return false;
        return HasAvailableSeat() || HasAvailableCouch();
    }

    public override Action[] GetActions(bool withPriority = false) {
        uint localNetId = PlayerController.Local?.netId ?? 0;
        if (IsLocalPlayerOccupying(localNetId)) return System.Array.Empty<Action>();

        return base.GetActions(withPriority).Where(a => {
            if (a.Type == ActionTypeEnum.SIT)   return HasAvailableSeat();
            if (a.Type == ActionTypeEnum.COUCH)  return HasAvailableCouch();
            return true;
        }).ToArray();
    }

    // ── ISeatBehavior ─────────────────────────────────────────────────────────

    public void RevokeSeat()  => SendPropInteraction(PropType.Seat, SeatInteraction.RevokeRequest);
    public void RevokeCouch() => SendPropInteraction(PropType.Seat, SeatInteraction.RevokeRequest);

    // ── PropBehaviourBase ─────────────────────────────────────────────────────

    protected override void Execute(Action action) {
        uint localNetId = PlayerController.Local?.netId ?? 0;
        if (IsLocalPlayerOccupying(localNetId)) return;

        switch (action.Type) {
            case ActionTypeEnum.SIT when HasAvailableSeat():
                SendPropInteraction(PropType.Seat, SeatInteraction.SitRequest);
                break;
            case ActionTypeEnum.COUCH when HasAvailableCouch():
                SendPropInteraction(PropType.Seat, SeatInteraction.CouchRequest);
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool HasAvailableSeat()  => _state.SeatOccupants  != null && System.Array.Exists(_state.SeatOccupants,  id => id == 0);
    private bool HasAvailableCouch() => _state.CouchOccupants != null && System.Array.Exists(_state.CouchOccupants, id => id == 0);

    private bool IsLocalPlayerOccupying(uint netId) {
        if (netId == 0) return false;
        return (_state.SeatOccupants  != null && System.Array.Exists(_state.SeatOccupants,  id => id == netId))
            || (_state.CouchOccupants != null && System.Array.Exists(_state.CouchOccupants, id => id == netId));
    }
}
