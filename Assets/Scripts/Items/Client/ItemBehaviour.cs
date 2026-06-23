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
    private Renderer[]   _renderers;

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
        _renderers = GetComponentsInChildren<Renderer>(true);
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
        // Restore visibility in case the item was hidden by an action while held
        // (e.g. dropped while the holder was sitting).
        SetRenderersVisible(true);
    }

    /// <summary>
    /// Toggle the visibility of every renderer on the item. Driven by PlayerAnimator
    /// when the holder performs an action that should hide held items (e.g. SIT, SLEEP).
    /// Uses Renderer.enabled to leave colliders / NavMeshObstacle / logic untouched.
    /// </summary>
    public void SetRenderersVisible(bool visible)
    {
        if (_renderers == null) return;
        foreach (var r in _renderers)
            if (r != null) r.enabled = visible;
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
            // Client-side hint: PICK est interdit si les mains sont pleines, SAUF si le
            // joueur tient un colis (l'item ramassé y est routé — capacité validée serveur).
            if (PlayerController.Local != null)
            {
                var hands = PlayerController.Local.PlayerHands;
                bool canAcquire = hands.CanHandleItem(_config.HandleType) || hands.IsHoldingContainer();
                foreach (var a in _groundActions)
                    if (a.Type == ActionTypeEnum.PICK)
                        a.IsForbidden = !canAcquire;
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
        _heldActions   = BuildHeldActions();

        SubscribeActions(_groundActions);
        SubscribeActions(_heldActions);
    }

    private Action[] BuildActions(System.Collections.Generic.List<Action> source)
    {
        if (source == null) return Array.Empty<Action>();
        return source.Select(Instantiate).ToArray();
    }

    // Actions « équipé » du config + l'action PLACE (« Poser ») injectée pour TOUT item tenu
    // (pas authorée par config, comme DROP) : poser l'item à un emplacement choisi après avoir
    // marché jusque-là. Cf. ItemPlacementController.
    private Action[] BuildHeldActions()
    {
        Action[] configured = BuildActions(_config.EquippedActions);
        Action place = LoadPlaceAction();
        if (place == null) return configured;

        var combined = new Action[configured.Length + 1];
        Array.Copy(configured, combined, configured.Length);
        combined[configured.Length] = place;
        return combined;
    }

    private static Action _placeActionTemplate;

    private static Action LoadPlaceAction()
    {
        if (_placeActionTemplate == null)
            _placeActionTemplate = Resources.Load<Action>("Configurations/Actions/PLACE");
        return _placeActionTemplate != null ? Instantiate(_placeActionTemplate) : null;
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

            case ActionTypeEnum.PLACE:
                ItemPlacementController.Instance.Begin(this);
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
