using Sim;
using Sim.Entities;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for DeliveryBox props.
/// Reads DeliveryBoxState for the delivery count (drives graphics).
/// When the server responds with S2C_DeliveryBoxOpened, ClientPropManager routes it here.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DeliveryBoxBehaviour : PropBehaviourBase {
    [Header("Visuals")]
    [SerializeField] private Transform  clapTransform;
    [SerializeField] private Quaternion openedClapRotation;
    [SerializeField] private GameObject package;

    public delegate void OpenEvent(DeliveryBoxBehaviour box, Delivery[] deliveries);
    public static event OpenEvent OnOpened;

    public delegate void UnPackageEvent(Delivery delivery);
    public static event UnPackageEvent UnPackage;

    private uint       _deliveryCount;
    private Delivery[] _lastDeliveries = System.Array.Empty<Delivery>();

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        base.ApplyState(type, payload);
        DeliveryBoxState state = DeliveryBoxState.Deserialize(payload);
        _deliveryCount = state.DeliveryCount;
        UpdateGraphics();
    }

    public void OnDeliveryBoxOpened(Delivery[] deliveries) {
        _lastDeliveries = deliveries ?? System.Array.Empty<Delivery>();
        OnOpened?.Invoke(this, _lastDeliveries);
    }

    /// <summary>
    /// Triggered from the UI when the user picks a specific delivery in the box.
    /// Goes into build mode via PlayerInteraction (subscribed to UnPackage).
    /// </summary>
    public void OpenDelivery(Delivery delivery) {
        if (_lastDeliveries == null || _lastDeliveries.Length == 0) {
            PlayerController.Local?.Idle();
            return;
        }
        UnPackage?.Invoke(delivery);
    }

    public Delivery[] Deliveries => _lastDeliveries;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public override bool IsInteractable() =>
        _deliveryCount > 0 && base.IsInteractable();

    public override Action[] GetActions(bool withPriority = false) {
        if (_deliveryCount == 0) return System.Array.Empty<Action>();
        return base.GetActions(withPriority);
    }

    // ── PropBehaviourBase ─────────────────────────────────────────────────────

    protected override void Execute(Action action) {
        if (action.Type == ActionTypeEnum.OPEN) {
            SendPropInteraction(PropType.DeliveryBox, DeliveryBoxInteraction.OpenRequest);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateGraphics() {
        if (clapTransform != null)
            clapTransform.localRotation = _deliveryCount > 0 ? openedClapRotation : Quaternion.identity;
        if (package != null)
            package.SetActive(_deliveryCount > 0);
    }
}
