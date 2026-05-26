using System;
using System.Linq;
using Interaction;
using Mirror;
using Sim;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;
using UnityEngine.AI;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side component on item GameObjects — replaces the old Item (NetworkBehaviour).
/// No NetworkIdentity, no SyncVar. State is driven by ItemNetworkMessages via ClientItemManager.
/// </summary>
[RequireComponent(typeof(ItemIdentity))]
public class ItemBehaviour : MonoBehaviour, IInteractable
{
    private ItemIdentity _identity;
    private ItemConfig   _config;
    private NavMeshObstacle _navObstacle;

    private bool    _isHeld;
    private uint    _holderNetId;
    private HandType _holderHand;

    // Cloned action ScriptableObjects — subscribed to OnExecute
    private Action[] _groundActions = Array.Empty<Action>();
    private Action[] _heldActions   = Array.Empty<Action>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        _identity = GetComponent<ItemIdentity>();
        _navObstacle = GetComponentInChildren<NavMeshObstacle>(true);
    }

    protected virtual void OnDestroy()
    {
        UnsubscribeActions(_groundActions);
        UnsubscribeActions(_heldActions);
    }

    // ── Initialization ────────────────────────────────────────────────────────

    public void Initialize(ItemConfig config)
    {
        _config = config;
        RebuildActions();
    }

    // ── Hand state (set by ClientItemManager) ─────────────────────────────────

    public virtual void OnAttachedToHand(uint holderNetId, HandType hand)
    {
        _isHeld      = true;
        _holderNetId = holderNetId;
        _holderHand  = hand;
        if (_navObstacle != null) _navObstacle.enabled = false;
    }

    public virtual void OnDetachedFromHand()
    {
        _isHeld      = false;
        _holderNetId = 0;
        if (_navObstacle != null) _navObstacle.enabled = true;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public float GetRange() => 1.5f;

    public bool IsInteractable()
    {
        // Un item tenu n'est jamais survolable/cliquable dans le monde (pour tous, local
        // ou distant). Ses actions d'item équipé (DROP, EAT…) passent par le HUD
        // d'inventaire, pas par le survol du modèle 3D en main.
        return !_isHeld;
    }

    public bool IsRightClickOnly() => false;

    public Action[] GetActions(bool withPriority = false)
    {
        if (!_isHeld)
        {
            // Client-side hint: mark PICK as forbidden when hands are full
            if (PlayerController.Local != null)
            {
                foreach (var a in _groundActions)
                    if (a.Type == ActionTypeEnum.PICK)
                        a.IsForbidden = !PlayerController.Local.PlayerHands.CanHandleItem(_config.HandleType);
            }
            return _groundActions;
        }

        if (_holderNetId == NetworkClient.connection?.identity?.netId)
            return _heldActions;

        return Array.Empty<Action>();
    }

    public void StopInteraction() { }

    // ── Properties ────────────────────────────────────────────────────────────

    public ItemIdentity Identity      => _identity;
    public ItemConfig   Configuration => _config;
    public bool         IsHeld        => _isHeld;
    public uint         HolderNetId   => _holderNetId;
    public HandType     HolderHand    => _holderHand;

    // ── Private helpers ───────────────────────────────────────────────────────

    private void RebuildActions()
    {
        UnsubscribeActions(_groundActions);
        UnsubscribeActions(_heldActions);

        _groundActions = BuildActions(_config.UnEquippedActions);
        _heldActions   = BuildActions(_config.EquippedActions);

        SubscribeActions(_groundActions);
        SubscribeActions(_heldActions);
    }

    private Action[] BuildActions(System.Collections.Generic.List<Action> source)
    {
        if (source == null) return Array.Empty<Action>();
        return source.Select(Instantiate).ToArray();
    }

    private void SubscribeActions(Action[] list)
    {
        foreach (var a in list) a.OnExecute += HandleAction;
    }

    private void UnsubscribeActions(Action[] list)
    {
        foreach (var a in list) a.OnExecute -= HandleAction;
    }

    private void HandleAction(Action action)
    {
        Debug.Log($"[ItemBehaviour] Action={action.Type} entity={_identity.EntityId}");

        switch (action.Type)
        {
            case ActionTypeEnum.PICK:
                NetworkClient.Send(new C2S_RequestPickupItem { EntityId = _identity.EntityId });
                break;

            case ActionTypeEnum.DROP:
                NetworkClient.Send(new C2S_RequestDropItem { Hand = _holderHand });
                break;

            case ActionTypeEnum.CLEAN:
                PlayerController.Local?.CleanItem(_identity.EntityId);
                break;

            default:
                HandleSpecialAction(action);
                break;
        }
    }

    /// <summary>Override in subclasses to handle item-type-specific actions (e.g. EAT, DRINK).</summary>
    protected virtual void HandleSpecialAction(Action action) { }
}
