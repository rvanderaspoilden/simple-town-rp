using System.Linq;
using Interaction;
using Sim;
using Sim.Building;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for PaintBucket props.
/// Reads PaintBucketState; fires OnOpened so the paint UI can display.
/// </summary>
[RequireComponent(typeof(PropsRenderer))]
public class PaintBucketBehaviour : MonoBehaviour, IPropBehaviour, IInteractable {
    [SerializeField] private float    interactRange = 1.5f;
    [SerializeField] private Action[] availableActions;

    public delegate void OpenEvent(PaintBucketBehaviour bucket);
    public static event OpenEvent OnOpened;

    private PropsRenderer    _renderer;
    private PaintBucketState _state;
    private Action[]         _runtimeActions;

    private void Awake() {
        _renderer = GetComponent<PropsRenderer>();

        _runtimeActions = availableActions
            .Where(a => a != null)
            .Select(Instantiate)
            .ToArray();

        foreach (var a in _runtimeActions) a.OnExecute += OnActionExecuted;
    }

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public void ApplyState(PropType type, byte[] payload) {
        _state = PaintBucketState.Deserialize(payload);
        _renderer.SetBuiltState(_state.Header.IsBuilt);
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public float    GetRange()                            => interactRange;
    public bool     IsInteractable()                      => _runtimeActions != null && _runtimeActions.Length > 0;
    public Action[] GetActions(bool withPriority = false) => _runtimeActions ?? System.Array.Empty<Action>();
    public void     StopInteraction()                     { }

    // ── State accessors ───────────────────────────────────────────────────────

    public int   PaintConfigId => _state.PaintConfigId;
    public Color GetColor()    => _state.Color;

    public CoverConfig GetPaintConfig() =>
        DatabaseManager.PaintDatabase?.GetPaintById(_state.PaintConfigId);

    public CoverSettings GetCoverSettings() =>
        new CoverSettings { paintConfigId = _state.PaintConfigId, additionalColor = _state.Color };

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnActionExecuted(Action action) {
        if (action.Type == Sim.Enums.ActionTypeEnum.PAINT) {
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
