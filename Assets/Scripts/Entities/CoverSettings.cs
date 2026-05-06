using System;
using UnityEngine;

[Serializable]
public struct CoverSettings {
    public int paintConfigId;
    public Color additionalColor;

    public bool Equals(PaintBucketBehaviour paintBucket) {
        return paintConfigId == paintBucket.PaintConfigId && additionalColor == paintBucket.GetColor();
    }

    public float[] GetColor() {
        return new float[4] {additionalColor.r, additionalColor.g, additionalColor.b, additionalColor.a};
    } 
}
