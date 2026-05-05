using System;
using System.Linq;
using Interaction;
using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for Dispenser props.
/// Opening the shop UI is client-local; purchasing sends C2S_PropInteraction to the server.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DispenserBehaviour : MonoBehaviour, IPropBehaviour, IInteractable {
    [SerializeField] private float                interactRange = 2f;
    [SerializeField] private Action[]             availableActions;
    [SerializeField] private DispenserConfiguration configuration;
    [SerializeField] private AudioClip            useSound;

    public delegate void OpenEvent(DispenserBehaviour dispenser);
    public static event OpenEvent OnOpened;

    public delegate void PurchaseResultEvent(int itemId, bool success);
    public static event PurchaseResultEvent OnPurchaseResult;

    private PropIdentity _identity;
    private Action[]     _runtimeActions;
    private AudioSource  _audio;

    private int PropId => _identity.PropId;

    private void Awake() {
        _identity = GetComponent<PropIdentity>();
        _audio    = GetComponent<AudioSource>();

        _runtimeActions = availableActions
            .Where(a => a != null)
            .Select(Instantiate)
            .ToArray();

        foreach (var a in _runtimeActions) a.OnExecute += OnActionExecuted;
    }

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public void ApplyState(PropType type, byte[] payload) {
        // DispenserState only carries the header — nothing visual to update here
    }

    public void HandlePurchaseResult(bool success, int itemId) {
        if (success) {
            _audio?.PlayOneShot(useSound, .3f);
            NotificationManager.Instance?.AddNotification("Vous avez acheté un item", NotificationType.BANK);
        }
        OnPurchaseResult?.Invoke(itemId, success);
    }

    public void BuyItem(ItemConfig itemConfig) {
        ClientPropManager.Instance?.RequestInteraction(
            PropId, PropType.Dispenser, DispenserInteraction.BuyRequest(itemConfig.ID)
        );
    }

    public DispenserConfiguration GetConfiguration() => configuration;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public float GetRange() => interactRange;

    public bool IsInteractable() =>
        _runtimeActions != null && _runtimeActions.Length > 0
        && PlayerController.Local != null
        && PlayerController.Local.PlayerHands.HasFreeHand();

    public Action[] GetActions(bool withPriority = false) {
        if (_runtimeActions == null) return Array.Empty<Action>();

        foreach (var a in _runtimeActions) {
            if (a.Type == ActionTypeEnum.USE) {
                a.IsForbidden = PlayerController.Local == null
                             || !PlayerController.Local.PlayerHands.HasFreeHand();
            }
        }
        return _runtimeActions;
    }

    public void StopInteraction() { }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnActionExecuted(Action action) {
        if (action.Type == ActionTypeEnum.USE) {
            OnOpened?.Invoke(this);
        }
    }

    private void OnDestroy() {
        if (_runtimeActions == null) return;
        foreach (var a in _runtimeActions) {
            if (a != null) a.OnExecute -= OnActionExecuted;
        }
    }
}
