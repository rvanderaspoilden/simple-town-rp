using UnityEngine;

/// <summary>
/// Server-side source for PaintBucket props. Initial state is empty (paintConfigId=-1);
/// when spawned via the build flow (delivery → C2S_BuildProp), an initial payload override
/// carries the actual paint config and color.
/// Reads defaultPresetId from sibling PropBehaviourBase if present.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class PaintBucketPropSource : ServerPropSource {
    public override PropType Type => PropType.PaintBucket;

    private PropBehaviourBase _behaviour;

    private void Awake() {
        _behaviour = GetComponent<PropBehaviourBase>();
    }

    public override byte[] GetInitialState() {
        int presetId = _behaviour != null ? _behaviour.DefaultPresetId : -1;
        return new PaintBucketState {
            Header        = new PropStateHeader { IsBuilt = true, PresetId = presetId },
            PaintConfigId = -1,
            R = 1f, G = 1f, B = 1f
        }.Serialize();
    }
}
