using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for Dispenser props.
/// Opening the shop UI is client-local; purchasing sends C2S_PropInteraction to the server.
/// and configuration (PropsConfig, from base) for actions/range/presets.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DispenserBehaviour : PropBehaviourBase {
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

    public DispenserConfiguration GetConfiguration() => base.configuration as DispenserConfiguration;

    // ── IInteractable ─────────────────────────────────────────────────────────

    // The dispenser remains interactable even when both hands are full:
    // the server-side purchase flow now spawns the bought item in the world
    // (room) as a fallback when no hand is free.
    // See PropInteractionRouter.HandleDispenser + ServerItemManager.SpawnItemInHand.

    // ── PropBehaviourBase ─────────────────────────────────────────────────────

    protected override void Execute(Action action) {
        if (action.Type == ActionTypeEnum.USE) {
            OnOpened?.Invoke(this);
        }
    }
    
    public override void StopInteraction() {
        DefaultViewUI.Instance.HidePropsContentUI();
    }
}
