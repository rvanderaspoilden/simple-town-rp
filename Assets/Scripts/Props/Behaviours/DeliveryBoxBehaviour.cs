using System.Linq;
using Interaction;
using Sim.Building;
using Sim.Entities;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for DeliveryBox props.
/// Reads DeliveryBoxState for the delivery count (drives graphics).
/// When the server responds with S2C_DeliveryBoxOpened, ClientPropManager routes it here.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DeliveryBoxBehaviour : MonoBehaviour, IPropBehaviour, IInteractable {
    [Header("Visuals")]
    [SerializeField] private Transform  clapTransform;
    [SerializeField] private Quaternion openedClapRotation;
    [SerializeField] private GameObject package;

    [Header("Interaction")]
    [SerializeField] private float    interactRange = 1.5f;
    [SerializeField] private Action[] availableActions;

    public delegate void OpenEvent(DeliveryBoxBehaviour box, Delivery[] deliveries);
    public static event OpenEvent OnOpened;

    private PropIdentity _identity;
    private uint         _deliveryCount;
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
        DeliveryBoxState state = DeliveryBoxState.Deserialize(payload);
        _deliveryCount = state.DeliveryCount;
        UpdateGraphics();
    }

    public void OnDeliveryBoxOpened(Delivery[] deliveries) {
        OnOpened?.Invoke(this, deliveries);
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public float GetRange() => interactRange;

    public bool IsInteractable() =>
        _deliveryCount > 0 && _runtimeActions != null && _runtimeActions.Length > 0;

    public Action[] GetActions(bool withPriority = false) {
        if (_deliveryCount == 0 || _runtimeActions == null)
            return System.Array.Empty<Action>();
        return _runtimeActions;
    }

    public void StopInteraction() { }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnActionExecuted(Action action) {
        if (action.Type == Sim.Enums.ActionTypeEnum.OPEN) {
            ClientPropManager.Instance?.RequestInteraction(
                PropId, PropType.DeliveryBox, DeliveryBoxInteraction.OpenRequest
            );
        }
    }

    private void UpdateGraphics() {
        if (clapTransform != null)
            clapTransform.localRotation = _deliveryCount > 0 ? openedClapRotation : Quaternion.identity;
        if (package != null)
            package.SetActive(_deliveryCount > 0);
    }

    private void OnDestroy() {
        if (_runtimeActions == null) return;
        foreach (var a in _runtimeActions) {
            if (a != null) a.OnExecute -= OnActionExecuted;
        }
    }
}
