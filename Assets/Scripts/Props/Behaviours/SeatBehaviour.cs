using System.Linq;
using Interaction;
using Sim;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for Seat props (chairs, benches, couches).
/// One SeatBehaviour = one seat slot.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class SeatBehaviour : MonoBehaviour, IPropBehaviour, IInteractable, ISeatBehavior {
    [Header("Seat")]
    [SerializeField] private Transform seatTransform;

    [Header("Actions")]
    [SerializeField] private float    interactRange = 2f;
    [SerializeField] private Action[] availableActions;

    private PropIdentity _identity;
    private SeatState    _state;
    private Action[]     _runtimeActions;

    private int PropId => _identity.PropId;

    private void Awake() {
        _identity = GetComponent<PropIdentity>();

        _runtimeActions = availableActions
            .Where(a => a != null)
            .Select(Instantiate)
            .ToArray();

        foreach (var a in _runtimeActions) a.OnExecute += OnActionExecuted;
    }

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public void ApplyState(PropType type, byte[] payload) {
        SeatState incoming = SeatState.Deserialize(payload);

        bool justOccupied = incoming.IsOccupied
                         && incoming.OccupantNetId == PlayerController.Local.netId
                         && !_state.IsOccupied;

        _state = incoming;

        if (justOccupied && seatTransform != null) {
            PlayerController.Local.Sit(this, seatTransform);
        }
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public float GetRange() => interactRange;

    public bool IsInteractable() =>
        !_state.IsOccupied && _runtimeActions != null && _runtimeActions.Length > 0;

    public Action[] GetActions(bool withPriority = false) {
        if (_state.IsOccupied || _runtimeActions == null)
            return System.Array.Empty<Action>();
        return _runtimeActions;
    }

    public void StopInteraction() { }

    // ── ISeatBehavior ─────────────────────────────────────────────────────────

    public void RevokeSeat() =>
        ClientPropManager.Instance?.RequestInteraction(PropId, PropType.Seat, SeatInteraction.RevokeRequest);

    public void RevokeCouch() => RevokeSeat();

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnActionExecuted(Action action) {
        if (_state.IsOccupied) return;

        switch (action.Type) {
            case Sim.Enums.ActionTypeEnum.SIT:
            case Sim.Enums.ActionTypeEnum.COUCH:
                ClientPropManager.Instance?.RequestInteraction(
                    PropId, PropType.Seat, SeatInteraction.SitRequest
                );
                break;
        }
    }

    private void OnDestroy() {
        if (_runtimeActions == null) return;
        foreach (var a in _runtimeActions) {
            if (a != null) a.OnExecute -= OnActionExecuted;
        }
    }
}
