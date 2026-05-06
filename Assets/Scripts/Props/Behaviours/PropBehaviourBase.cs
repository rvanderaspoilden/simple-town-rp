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

    protected int PropId => _identity.PropId;

    /// <summary>
    /// Fired when the local player triggers a SELL action on a new-system prop.
    /// PlayerInteraction subscribes and sends C2S_RemoveProp.
    /// </summary>
    public static event System.Action<PropBehaviourBase> OnSellRequest;

    /// <summary>
    /// Fired when the local player triggers a MOVE action on a new-system prop.
    /// PlayerInteraction subscribes and enters MOVING_PROPS state via BuildManager.
    /// </summary>
    public static event System.Action<PropBehaviourBase> OnMoveRequest;

    public int DefaultPresetId => defaultPresetId;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        _identity = GetComponent<PropIdentity>();
        _renderer = GetComponent<PropsRenderer>();
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
        Action[] acts = _isBuilt ? _builtActions : _unbuiltActions;
        return acts != null && acts.Length > 0;
    }

    public virtual Action[] GetActions(bool withPriority = false)
    {
        Action[] acts = _isBuilt ? _builtActions : _unbuiltActions;
        if (acts == null || acts.Length == 0) return System.Array.Empty<Action>();

        bool hasPerm = _apartment == null
                       || _apartment.IsTenant(PlayerController.Local?.CharacterData);

        return acts.Where(a =>
            (!a.NeedPermission || hasPerm) &&
            (!withPriority || (a.Type != ActionTypeEnum.SELL && a.Type != ActionTypeEnum.MOVE))
        ).ToArray();
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

    public void SetConfiguration(PropsConfig config) =>
        configuration = config;

    public PropsConfig GetConfiguration() =>
        configuration;

    protected void ApplyHeader(PropStateHeader header)
    {
        bool wasBuilt = _isBuilt;
        _isBuilt = header.IsBuilt;

        if (_renderer != null)
        {
            _renderer.SetBuiltState(header.IsBuilt);

            if (header.PresetId >= 0 && configuration?.Presets != null)
            {
                PropsPreset preset = configuration.Presets.FirstOrDefault(p => p.ID == header.PresetId);
                if (preset != null) _renderer.SetPreset(preset);
            }
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
    }

    private void UnsubscribeActions(Action[] actions)
    {
        if (actions == null) return;
        foreach (var a in actions)
            if (a != null)
                a.OnExecute -= DoAction;
    }

    private void DoAction(Action action)
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

            case ActionTypeEnum.SELL:
                OnSellRequest?.Invoke(this);
                break;

            case ActionTypeEnum.MOVE:
                OnMoveRequest?.Invoke(this);
                break;

            default:
                Execute(action);
                break;
        }
    }
}