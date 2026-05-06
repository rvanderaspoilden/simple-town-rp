using UnityEngine;

/// <summary>
/// Server-side source for PaintBucket props. Initial state is empty (paintConfigId=-1);
/// when spawned via the build flow (delivery → C2S_BuildProp), an initial payload override
/// carries the actual paint config and color.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class PaintBucketPropSource : ServerPropSource {
    public override PropType Type => PropType.PaintBucket;

    public override byte[] GetInitialState() =>
        new PaintBucketState {
            Header        = PropStateHeader.Default,
            PaintConfigId = -1,
            R = 1f, G = 1f, B = 1f
        }.Serialize();
}
