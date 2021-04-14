using System;
using UnityEngine;

[Serializable]
public struct WallFace {
    public int sharedMaterialIdx;

    public int paintConfigId;

    public Color additionalColor;

    public WallFace(int sharedMaterialIdx, int paintConfigId, Color additionalColor) {
        this.sharedMaterialIdx = sharedMaterialIdx;
        this.paintConfigId = paintConfigId;
        this.additionalColor = additionalColor;
    }
}