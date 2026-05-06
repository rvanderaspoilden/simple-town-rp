using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for Dispenser props.
/// Opening the shop UI is client-local; purchasing sends C2S_PropInteraction to the server.
/// Note: assign dispenserConfig (DispenserConfiguration) for the item catalog,
/// and configuration (PropsConfig, from base) for actions/range/presets.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DispenserBehaviour : PropBehaviourBase {
    [SerializeField] private DispenserConfiguration dispenserConfig;
    [SerializeField] private AudioClip              useSound;

    public delegate void OpenEvent(DispenserBehaviour dispenser);
    public static event OpenEvent OnOpened;

    public delegate void PurchaseResultEvent(int itemId, bool success);
    public static event PurchaseResultEvent OnPurchaseResult;

    private AudioSource _audio;

    protected override void Awake() {
        base.Awake();
        _audio = GetComponent<AudioSource>();
    }

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        base.ApplyState(type, payload);
        // DispenserState only carries the header — nothing extra to update
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void HandlePurchaseResult(bool success, int itemId) {
        if (success) {
            _audio?.PlayOneShot(useSound, .3f);
            NotificationManager.Instance?.AddNotification("Vous avez acheté un item", NotificationType.BANK);
        }
        OnPurchaseResult?.Invoke(itemId, success);
    }

    public void BuyItem(ItemConfig itemConfig) {
        SendPropInteraction(PropType.Dispenser, DispenserInteraction.BuyRequest(itemConfig.ID));
    }

    public DispenserConfiguration GetConfiguration() => dispenserConfig;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public override bool IsInteractable() =>
        base.IsInteractable()
        && PlayerController.Local != null
        && PlayerController.Local.PlayerHands.HasFreeHand();

    public override Action[] GetActions(bool withPriority = false) {
        Action[] acts = base.GetActions(withPriority);
        foreach (var a in acts) {
            if (a.Type == ActionTypeEnum.USE) {
                a.IsForbidden = PlayerController.Local == null
                             || !PlayerController.Local.PlayerHands.HasFreeHand();
            }
        }
        return acts;
    }

    // ── PropBehaviourBase ─────────────────────────────────────────────────────

    protected override void Execute(Action action) {
        if (action.Type == ActionTypeEnum.USE) {
            OnOpened?.Invoke(this);
        }
    }
}
