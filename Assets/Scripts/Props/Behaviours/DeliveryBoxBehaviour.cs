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

    // Action « Récupérer » : ouvre la grille des COLIS ITEMS de la boîte (place
    // container:deliverybox:{charId}) — les achats qui ne tenaient pas dans l'inventaire
    // (RÈGLE PROJET) atterrissent ici. Grille conteneur standard, même flux que la fouille.
    private Action _collectAction;

    protected override void Awake() {
        base.Awake();
        _collectAction = Action.CreateRuntime(ActionTypeEnum.SEARCH, "Récupérer",
            Resources.Load<Action>("Configurations/Actions/SEARCH")?.Icon);
        _collectAction.OnExecute += OnCollectExecuted;
    }

    protected override void OnDestroy() {
        base.OnDestroy();
        if (_collectAction != null) _collectAction.OnExecute -= OnCollectExecuted;
    }

    private void OnCollectExecuted(Action _) {
        if (!Mirror.NetworkClient.isConnected) return;
        // BUSY avant la grille (la sortie d'Idle referme l'inventaire) ; annulable au mouvement.
        PlayerController.Local?.BeginBusy(LootPanelUI.IsLootOpen, transform,
            cancelOnMove: true, onCancelled: () => LootPanelUI.Instance?.Close());
        LootPanelUI.Instance?.OpenSearchDelayed(ServerItemManager.DeliveryBoxSlots,
            "Boîte de livraison", 0.1f);
        Mirror.NetworkClient.Send(new C2S_OpenDeliveryBoxItems());
    }

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        base.ApplyState(type, payload);
        DeliveryBoxState state = DeliveryBoxState.Deserialize(payload);
        _deliveryCount = state.DeliveryCount;
        Debug.Log($"[DeliveryBox] ApplyState propId={GetComponent<PropIdentity>()?.PropId} count={_deliveryCount} packageActive={(package != null ? package.activeSelf : false)}");
        UpdateGraphics();
    }

    public void OnDeliveryBoxOpened(Delivery[] deliveries) {
        _lastDeliveries = deliveries ?? System.Array.Empty<Delivery>();
        // The list is authoritative — keep _deliveryCount in sync with it so the
        // visuals (clap rotation + package GameObject) update even if the matching
        // S2C_PropUpdate hasn't been processed yet.
        uint listCount = (uint)_lastDeliveries.Length;
        if (_deliveryCount != listCount) {
            _deliveryCount = listCount;
            UpdateGraphics();
        }
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

    // Only the box owner (the apartment tenant) can interact with it.
    public override bool IsInteractable() => IsOwnedByLocal && base.IsInteractable();

    public override Action[] GetActions(bool withPriority = false) {
        Action[] actions = base.GetActions(withPriority);
        if (_collectAction == null) return actions;
        var result = new Action[actions.Length + 1];
        actions.CopyTo(result, 0);
        result[actions.Length] = _collectAction;
        return result;
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
