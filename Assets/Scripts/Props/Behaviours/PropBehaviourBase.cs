using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Interaction;
using Sim;
using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Base class for all new-system prop behaviours.
///   - reads PropsConfig for actions, range and presets
///   - drives PropsRenderer for built/unbuilt visuals and preset changes
///   - dispatches base actions: LOOK (client-local), BUILD/MOVE/SELL (C2S messages or static events)
///   - delegates type-specific actions to Execute()
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public abstract class PropBehaviourBase : MonoBehaviour, IPropBehaviour, IInteractable
{
    [Header("Configuration")] [SerializeField]
    protected PropsConfig configuration;

    [Header("Settings")] [SerializeField]
    protected int defaultPresetId = -1;

    protected PropIdentity _identity;
    protected PropsRenderer _renderer; // may be null (e.g. pure trigger props)
    protected ApartmentController _apartment; // null for city props

    protected Action[] _builtActions;
    protected Action[] _unbuiltActions;

    private bool _isBuilt = true;

    // Magasin physique : prop d'expo (City) avec un composant ShopDisplay. Mis en
    // cache au Awake pour injecter l'action BUY sans check de propriété.
    private bool _isShopDisplay;

    /// <summary>True si ce prop est un article d'exposition d'un magasin physique (composant ShopDisplay).</summary>
    public bool IsShopDisplay => _isShopDisplay;

    protected int PropId => _identity.PropId;

    /// <summary>
    /// Fired when the owner triggers DESTROY on a built toBuild prop. The confirmation
    /// dialog (DestroyConfirmUI) subscribes, asks for confirmation, then sends C2S_DestroyProp.
    /// </summary>
    public static event System.Action<PropBehaviourBase> OnDestroyRequest;

    /// <summary>
    /// Fired when the local player triggers a MOVE action on a new-system prop.
    /// PlayerInteraction subscribes and enters MOVING_PROPS state via BuildManager.
    /// </summary>
    public static event System.Action<PropBehaviourBase> OnMoveRequest;

    /// <summary>
    /// Fired when the owner triggers LIST_FOR_SALE. The price-input UI subscribes,
    /// collects a price and emits C2S_SetPropForSale.
    /// </summary>
    public static event System.Action<PropBehaviourBase> OnListForSaleRequest;

    /// <summary>Fired when the owner triggers UNLIST. PlayerInteraction sends C2S_UnlistProp.</summary>
    public static event System.Action<PropBehaviourBase> OnUnlistRequest;

    /// <summary>Fired when a visitor triggers BUY. PlayerInteraction opens the confirm fiche.</summary>
    public static event System.Action<PropBehaviourBase> OnBuyRequest;

    // ── Client-side sale state (mirrors the server's S2C_PropSaleState) ─────────
    private bool   _forSale;
    private int    _price;
    private string _reservedByName;
    private string _ownerCharId;
    private PropSaleBillboard _saleBillboard;

    public bool   ForSale        => _forSale;
    public int    Price          => _price;
    public string ReservedByName => _reservedByName;
    public bool   IsReserved     => !string.IsNullOrEmpty(_reservedByName);

    /// <summary>True when the local player owns this prop (matches the broadcast owner id).</summary>
    protected bool IsOwnedByLocal =>
        !string.IsNullOrEmpty(_ownerCharId)
        && PlayerController.Local?.CharacterData?.Id == _ownerCharId;

    /// <summary>Owner (apartment tenant) broadcast by the server with the prop spawn. "" for city/unowned.</summary>
    public void SetOwner(string ownerCharId) {
        if (!string.IsNullOrEmpty(ownerCharId)) _ownerCharId = ownerCharId;
    }

    private bool IsApartmentRoom() =>
        _identity != null && _identity.RoomId != "city";

    // Sale Actions injected dynamically (see SaleActionsConfig). Instantiated once
    // in SetupActions and subscribed to DoAction like the config actions.
    private Action _actListForSale, _actUnlist, _actBuy;

    // Generic owner actions (BUILD / MOVE / DESTROY) injected dynamically — same idea as
    // the sale actions, so they no longer have to be hand-added to every PropsConfig.
    // Visible only to the prop owner (IsOwnedByLocal). Per-instance copies wired to DoAction.
    // NB: selling is handled exclusively by the P2P LIST_FOR_SALE flow (GetSaleActions),
    // which is gated on PropsConfig.IsSellable(); there is no generic "sell/remove" action.
    private Action _actBuild, _actMove, _actDestroy;

    // Shared Action "prototypes" loaded once from Resources (assets live under
    // Assets/Resources/Configurations/Actions/). Instantiated per prop instance.
    private static Action _protoBuild, _protoMove, _protoDestroy;
    private static bool   _genericProtosLoaded;

    private static void LoadGenericActionPrototypes()
    {
        if (_genericProtosLoaded) return;
        _protoBuild   = Resources.Load<Action>("Configurations/Actions/BUILD");
        _protoMove    = Resources.Load<Action>("Configurations/Actions/MOVE");
        _protoDestroy = Resources.Load<Action>("Configurations/Actions/DESTROY");
        _genericProtosLoaded = true;
    }

    public int DefaultPresetId => defaultPresetId;

    public void SetDefaultPresetId(int id) { defaultPresetId = id; }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        _identity = GetComponent<PropIdentity>();
        _renderer = GetComponent<PropsRenderer>();
        _isShopDisplay = GetComponent<ShopDisplay>() != null;
        SetupActions();
    }

    protected virtual void Start()
    {
        _apartment = GetComponentInParent<ApartmentController>();
    }

    protected virtual void OnDestroy()
    {
        UnsubscribeActions(_builtActions);
        UnsubscribeActions(_unbuiltActions);
        UnsubscribeActions(new[] { _actListForSale, _actUnlist, _actBuy });
        UnsubscribeActions(new[] { _actBuild, _actMove, _actDestroy });
    }

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public virtual void ApplyState(PropType type, byte[] payload)
    {
        PropStateHeader header = PropStateHeader.ReadFrom(payload);
        ApplyHeader(header);
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public virtual float GetRange() =>
        configuration != null ? configuration.GetRangeToInteract() : 2f;

    public virtual bool IsInteractable()
    {
        if (!enabled || !gameObject.activeInHierarchy) return false;
        // Sale actions can make an otherwise action-less prop interactable for a
        // visitor (Buy) or owner (List/Unlist), so consider the full set.
        return GetActions().Length > 0;
    }

    /// <summary>
    /// Applies the network sale state (S2C_PropSaleState) on the client. Drives the
    /// optional price billboard and refreshes which contextual actions are offered.
    /// </summary>
    public virtual void ApplySaleState(bool forSale, int price, string reservedByName, string ownerCharId)
    {
        _forSale        = forSale;
        _price          = price;
        _reservedByName = reservedByName;
        if (!string.IsNullOrEmpty(ownerCharId)) _ownerCharId = ownerCharId;

        // Floating "À vendre" billboard above the prop. Created lazily the first
        // time the prop becomes for-sale; kept hidden / range-gated otherwise.
        if (forSale && _saleBillboard == null)
            _saleBillboard = SpawnSaleBillboard();
        if (_saleBillboard != null)
            _saleBillboard.SetState(forSale, price, reservedByName);

        OnSaleStateChanged(forSale, price, reservedByName);
    }

    /// <summary>
    /// Hook for visuals beyond the built-in billboard. Default no-op — subclasses or
    /// a sibling component can override/listen.
    /// </summary>
    protected virtual void OnSaleStateChanged(bool forSale, int price, string reservedByName) { }

    /// <summary>
    /// Instantiates the sale billboard: the prefab from SaleActionsConfig if assigned,
    /// otherwise a procedurally-built one. Parented under this prop and bound to it.
    /// </summary>
    private PropSaleBillboard SpawnSaleBillboard()
    {
        GameObject prefab = SaleActionsConfig.Get()?.billboardPrefab;
        GameObject go;
        if (prefab != null) {
            go = Instantiate(prefab, transform);
        } else {
            go = new GameObject("SaleBillboard");
            go.transform.SetParent(transform, false);
        }

        PropSaleBillboard billboard = go.GetComponent<PropSaleBillboard>();
        if (billboard == null) billboard = go.AddComponent<PropSaleBillboard>();
        billboard.Init(this);
        return billboard;
    }

    public virtual bool IsRightClickOnly() =>
        configuration != null && configuration.IsRightClickOnly();

    public virtual Action[] GetActions(bool withPriority = false)
    {
        // Magasin physique : un prop d'expo ne propose QUE l'action BUY (acheter).
        // Toujours forSale par construction (ShopDisplay = stock infini), donc on ne
        // dépend pas du flag _forSale. BUY est l'action PRIORITAIRE (clic gauche direct)
        // ET la seule disponible en radial — court-circuite les actions du config (LOOK…),
        // l'autorité reste serveur.
        if (_isShopDisplay)
        {
            return (_isBuilt && _actBuy != null)
                ? new[] { _actBuy }
                : System.Array.Empty<Action>();
        }

        Action[] acts = _isBuilt ? _builtActions : _unbuiltActions;
        acts ??= System.Array.Empty<Action>();

        // Owner-only actions (NeedPermission, e.g. MOVE) are gated by the broadcast
        // owner id: in an apartment, only the owner sees them. City props and props
        // whose owner is unknown (fixtures with no owner set) stay lenient as before.
        bool hasPerm = !IsApartmentRoom()
                       || string.IsNullOrEmpty(_ownerCharId)
                       || IsOwnedByLocal;

        IEnumerable<Action> result = acts.Where(a =>
            (!a.NeedPermission || hasPerm) &&
            (!withPriority || (a.Type != ActionTypeEnum.SELL && a.Type != ActionTypeEnum.MOVE))
        );

        // Inject cross-cutting sale actions on sellable furniture inside apartments
        // (never on city scene props, doors, lights, boxes, packages…). Authority
        // (owner vs visitor) is enforced server-side, mirroring how SELL/MOVE already
        // work — _apartment is null on clients for runtime props.
        if (_isBuilt && IsSellableApartmentProp())
            result = result.Concat(GetSaleActions(withPriority));

        // Inject generic owner actions (BUILD / MOVE / SELL). Single rule: the local
        // player must own the prop → excludes City / shop / unowned props automatically.
        //   BUILD : non-built state only, gated on the toBuild flag (action primaire, clic gauche ok)
        //   MOVE + SELL : built state only, clic droit (cohérent avec le filtre withPriority)
        if (IsOwnedByLocal)
            result = result.Concat(GetGenericOwnerActions(withPriority));

        return result.ToArray();
    }

    /// <summary>True for sellable furniture living in an apartment/hall room.</summary>
    private bool IsSellableApartmentProp() =>
        configuration != null && configuration.IsSellable()
        && _identity != null && _identity.RoomId != "city";

    /// <summary>
    /// Contextual sale actions, gated by the broadcast owner id:
    ///   for-sale     → owner sees Unlist, others see Buy (if not reserved);
    ///   not-for-sale → owner sees List + Give; others see nothing.
    /// Hidden under withPriority (left-click) — sale is a deliberate right-click act.
    /// </summary>
    private IEnumerable<Action> GetSaleActions(bool withPriority)
    {
        if (withPriority) yield break;

        bool isOwner = IsOwnedByLocal;

        if (_forSale)
        {
            if (isOwner)
            {
                if (_actUnlist != null) yield return _actUnlist;
            }
            else if (!IsReserved && _actBuy != null)
            {
                yield return _actBuy;
            }
        }
        else if (isOwner)
        {
            if (_actListForSale != null) yield return _actListForSale;
        }
    }

    /// <summary>
    /// Generic owner actions injected without any PropsConfig entry. Caller already
    /// checked IsOwnedByLocal.
    ///   non-built + toBuild → BUILD (also offered on left-click, it's the primary act);
    ///   built               → MOVE + DESTROY (right-click only, like the sale actions).
    /// Selling is NOT here — it's the P2P LIST_FOR_SALE flow, gated on IsSellable().
    /// </summary>
    private IEnumerable<Action> GetGenericOwnerActions(bool withPriority)
    {
        if (!_isBuilt)
        {
            if (configuration != null && configuration.MustBeBuilt() && _actBuild != null)
                yield return _actBuild;
            yield break;
        }

        if (withPriority) yield break; // MOVE/DESTROY are deliberate right-click acts

        // MOVE: on by default; fixtures (delivery box, doors…) untick PropsConfig.movable.
        if (_actMove != null && (configuration == null || configuration.IsMovable()))
            yield return _actMove;

        // DESTROY: une fois construit, sur les props constructibles (toBuild) ET sur les
        // props non-constructibles déplaçables (ex. déco murale toBuild=false). Exclut les
        // fixtures non déplaçables (portes, boîte de livraison, lumières intégrées :
        // movable=false). Irréversible — confirmé client-side, re-vérifié (OwnsProp) serveur.
        if (_actDestroy != null && configuration != null
            && (configuration.MustBeBuilt() || configuration.IsMovable()))
            yield return _actDestroy;
    }

    public virtual void StopInteraction()
    {
    }

    // ── Subclass hook ─────────────────────────────────────────────────────────

    /// <summary>
    /// Override to handle type-specific actions (SIT, COUCH, OPEN, USE, PAINT…).
    /// Base actions (LOOK, BUILD, MOVE, SELL) are already handled in DoAction().
    /// </summary>
    protected virtual void Execute(Action action)
    {
    }

    // ── Helpers accessible to subclasses ──────────────────────────────────────

    protected void SendPropInteraction(PropType type, byte[] payload) =>
        ClientPropManager.Instance?.RequestInteraction(PropId, type, payload);

    public bool IsBuilt() => _isBuilt;

    public bool IsWallProps() =>
        configuration?.GetSurfaceToPose() == BuildSurfaceEnum.WALL;

    public bool IsGroundProps() =>
        configuration?.GetSurfaceToPose() == BuildSurfaceEnum.GROUND;

    public bool IsRoofProps() =>
        configuration?.GetSurfaceToPose() == BuildSurfaceEnum.ROOF;

    public void SetConfiguration(PropsConfig config) =>
        configuration = config;

    public PropsConfig GetConfiguration() =>
        configuration;

    /// <summary>
    /// Applies the given preset visually without changing the network state.
    /// Call after SetConfiguration so the lookup uses the correct Presets array.
    /// </summary>
    public void ApplyPresetVisual(int presetId)
    {
        if (presetId < 0 || _renderer == null || configuration?.Presets == null) return;
        PropsPreset preset = configuration.Presets.FirstOrDefault(p => p.ID == presetId);
        if (preset != null) _renderer.SetPreset(preset);
    }

    protected void ApplyHeader(PropStateHeader header)
    {
        bool wasBuilt = _isBuilt;
        _isBuilt = header.IsBuilt;

        if (_renderer != null)
        {
            _renderer.SetBuiltState(header.IsBuilt);

            if (header.PresetId >= 0)
            {
                if (configuration == null)
                {
                    Debug.LogWarning($"[PropsRenderer] Cannot apply presetId={header.PresetId} on {name}: configuration is null");
                }
                else if (configuration.Presets == null || configuration.Presets.Length == 0)
                {
                    Debug.LogWarning($"[PropsRenderer] Cannot apply presetId={header.PresetId} on {name}: Presets array is null or empty in PropsConfig '{configuration.name}'");
                }
                else
                {
                    PropsPreset preset = configuration.Presets.FirstOrDefault(p => p.ID == header.PresetId);
                    if (preset != null)
                    {
                        Debug.Log($"[PropsRenderer] Applying presetId={header.PresetId} on {name}");
                        _renderer.SetPreset(preset);
                        Debug.Log($"[PropsRenderer] Visual refresh completed presetId={header.PresetId} on {name}");
                    }
                    else
                    {
                        Debug.LogWarning($"[PropsRenderer] Preset id={header.PresetId} not found in PropsConfig '{configuration.name}' (available: [{string.Join(", ", configuration.Presets.Select(p => p.ID))}])");
                    }
                }
            }
        }
        else if (header.PresetId >= 0)
        {
            Debug.LogWarning($"[PropsRenderer] presetId={header.PresetId} ignored on {name}: no PropsRenderer component");
        }

        if (!wasBuilt && _isBuilt) OnJustBuilt();
    }

    /// <summary>
    /// Called once when isBuilt transitions false → true.
    /// Default behaviour: scale-up bounce + plays the configuration's BuildSound (DOTween).
    /// Override and call base for custom additions.
    /// </summary>
    protected virtual void OnJustBuilt()
    {
        transform.localScale = Vector3.zero;
        transform.DOScale(Vector3.one, 1f)
            .SetEase(Ease.OutBounce);

        AudioSource src = GetComponent<AudioSource>();
        if (src != null && configuration != null && configuration.BuildSound != null)
        {
            src.PlayOneShot(configuration.BuildSound);
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void SetupActions()
    {
        if (configuration == null)
        {
            _builtActions = System.Array.Empty<Action>();
            _unbuiltActions = System.Array.Empty<Action>();
            return;
        }

        _builtActions = configuration.GetActions().Where(a => a != null).Select(Instantiate).ToArray();
        _unbuiltActions = configuration.GetUnbuiltActions().Where(a => a != null).Select(Instantiate).ToArray();

        foreach (var a in _builtActions) a.OnExecute += DoAction;
        foreach (var a in _unbuiltActions) a.OnExecute += DoAction;

        SetupSaleActions();
        SetupGenericActions();
    }

    /// <summary>
    /// Instantiates per-instance copies of the shared sale Actions and wires them to
    /// DoAction. No-op if the SaleActionsConfig asset isn't present yet.
    /// </summary>
    private void SetupSaleActions()
    {
        SaleActionsConfig cfg = SaleActionsConfig.Get();
        if (cfg == null) return;

        _actListForSale = InstantiateAction(cfg.listForSale);
        _actUnlist      = InstantiateAction(cfg.unlist);
        _actBuy         = InstantiateAction(cfg.buy);
    }

    /// <summary>
    /// Instantiates per-instance copies of the generic owner Actions (BUILD/MOVE/SELL)
    /// and wires them to DoAction. They're injected in GetActions, not stored in any
    /// PropsConfig. No-op for actions whose Resources asset is missing.
    /// </summary>
    private void SetupGenericActions()
    {
        LoadGenericActionPrototypes();
        _actBuild   = InstantiateAction(_protoBuild);
        _actMove    = InstantiateAction(_protoMove);
        _actDestroy = InstantiateAction(_protoDestroy);
    }

    /// <summary>
    /// Instantiates a per-instance copy of a shared Action prototype and wires it to
    /// <see cref="DoAction"/>. Used for the injected sale/generic actions and exposed to
    /// subclasses that inject their own type-specific actions (e.g. SeatBehaviour's SIT/COUCH).
    /// </summary>
    protected Action InstantiateAction(Action source)
    {
        if (source == null) return null;
        Action copy = Instantiate(source);
        copy.OnExecute += DoAction;
        return copy;
    }

    private void UnsubscribeActions(Action[] actions)
    {
        if (actions == null) return;
        foreach (var a in actions)
            if (a != null)
                a.OnExecute -= DoAction;
    }

    protected void DoAction(Action action)
    {
        switch (action.Type)
        {
            case ActionTypeEnum.LOOK:
                PlayerController.Local?.Look(transform);
                break;

            case ActionTypeEnum.BUILD:
                // Sent to server; server updates isBuilt in the payload header and broadcasts state
                SendPropInteraction(PropType.Generic, GenericPropInteraction.BuildRequest);
                break;

            case ActionTypeEnum.DESTROY:
                // No direct network send: the confirmation dialog emits C2S_DestroyProp on confirm.
                OnDestroyRequest?.Invoke(this);
                break;

            case ActionTypeEnum.MOVE:
                OnMoveRequest?.Invoke(this);
                break;

            case ActionTypeEnum.LIST_FOR_SALE:
                OnListForSaleRequest?.Invoke(this);
                break;

            case ActionTypeEnum.UNLIST:
                OnUnlistRequest?.Invoke(this);
                break;

            case ActionTypeEnum.BUY:
                OnBuyRequest?.Invoke(this);
                break;

            default:
                Execute(action);
                break;
        }
    }
}