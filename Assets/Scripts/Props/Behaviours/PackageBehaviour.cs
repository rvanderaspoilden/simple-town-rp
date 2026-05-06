using Sim;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for Package props.
/// Contains a PropsConfig that is unpacked when opened.
/// Opening triggers build mode via the OnOpened event.
/// The PropsConfig ID is received via network state from the server.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class PackageBehaviour : PropBehaviourBase
{
    [Header("Settings")] [SerializeField] private AudioClip openSound;
    [SerializeField] private PropsConfig propsInside; // Editor-assigned fallback (for scene props only)

    public delegate void OpenEvent(PackageBehaviour package);

    public static event OpenEvent OnOpened;

    private AudioSource _audio;
    private int _propsConfigId; // Received from server state

    protected override void Awake()
    {
        base.Awake();
        _audio = GetComponent<AudioSource>();
    }

    /// <summary>
    /// Applies the network state to this package.
    /// Resolves the PropsConfig from the ID received from server.
    /// </summary>
    public override void ApplyState(PropType type, byte[] payload)
    {
        base.ApplyState(type, payload);
        if (type == PropType.Package)
        {
            PackageState state = PackageState.Deserialize(payload);
            _propsConfigId = state.PropsConfigId;
        }
    }

    /// <summary>
    /// Returns the PropsConfig contained in this package.
    /// First tries to resolve from network state ID, falls back to editor-assigned value.
    /// Used by BuildManager when unpacking.
    /// </summary>
    public PropsConfig GetPropsConfigInside()
    {
        if (_propsConfigId > 0)
        {
            return DatabaseManager.PropsDatabase?.GetPropsById(_propsConfigId);
        }
        return propsInside;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public override bool IsInteractable() =>
        base.IsInteractable()
        && PlayerController.Local != null
        && PlayerController.Local.PlayerHands.HasFreeHand();

    public override Action[] GetActions(bool withPriority = false)
    {
        Action[] acts = base.GetActions(withPriority);
        foreach (var a in acts)
        {
            if (a.Type == ActionTypeEnum.OPEN)
            {
                a.IsForbidden = PlayerController.Local == null
                                || !PlayerController.Local.PlayerHands.HasFreeHand();
            }
        }

        return acts;
    }

    // ── PropBehaviourBase ─────────────────────────────────────────────────────

    protected override void Execute(Action action)
    {
        if (action.Type == ActionTypeEnum.OPEN)
        {
            _audio?.PlayOneShot(openSound, .3f);
            OnOpened?.Invoke(this);
        }
    }

    public override void StopInteraction()
    {
        // Package opening immediately triggers build mode,
        // no specific UI to close here
    }
}