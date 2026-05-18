using Sim;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for PaintBucket props.
/// Reads PaintBucketState; fires OnOpened so the paint UI can display.
/// Opening the UI is client-local — no C2S message is sent from here.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class PaintBucketBehaviour : PropBehaviourBase {
    public delegate void OpenEvent(PaintBucketBehaviour bucket);
    public static event OpenEvent OnOpened;

    private PaintBucketState _state;

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        base.ApplyState(type, payload);
        _state = PaintBucketState.Deserialize(payload);
    }

    // ── State accessors ───────────────────────────────────────────────────────

    public int   PaintConfigId => _state.PaintConfigId;
    public Color GetColor()    => _state.Color;

    public CoverConfig GetPaintConfig() =>
        DatabaseManager.GetPaintById(_state.PaintConfigId);

    public CoverSettings GetCoverSettings() =>
        new CoverSettings { paintConfigId = _state.PaintConfigId, additionalColor = _state.Color };

    // ── PropBehaviourBase ─────────────────────────────────────────────────────

    protected override void Execute(Action action) {
        if (action.Type == ActionTypeEnum.PAINT) {
            OnOpened?.Invoke(this);
        }
    }
}
