using System;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Wire format for a single cover (wall or ground tile paint). Serialized
    /// across C2S_ApplyWall/GroundCovers and the new /covers backend endpoint.
    /// Carries the surface index, the paint config id, and an RGBA tint.
    /// </summary>
    [Serializable]
    public struct CoverData {
        public int     idx;
        public int     paintConfigId;
        public float[] additionalColor;

        public Color GetColor() {
            if (additionalColor != null && additionalColor.Length > 3) {
                return new Color(additionalColor[0], additionalColor[1], additionalColor[2]);
            }
            return Color.white;
        }
    }
}
