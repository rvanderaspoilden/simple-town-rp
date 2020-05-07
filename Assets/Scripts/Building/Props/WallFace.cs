using System;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    [Serializable]
    public class WallFace {
        [SerializeField] private int sharedMaterialIdx;
        [SerializeField] private int paintConfigId;
        [SerializeField] private Color additionalColor;

        public WallFace(int sharedMaterialIdx, int paintConfigId, Color additionalColor) {
            this.sharedMaterialIdx = sharedMaterialIdx;
            this.paintConfigId = paintConfigId;
            this.additionalColor = additionalColor;
        }

        public WallFace(WallFace source) {
            this.sharedMaterialIdx = source.sharedMaterialIdx;
            this.paintConfigId = source.paintConfigId;
            this.additionalColor = source.additionalColor;
        }

        public PaintConfig GetPaintConfig() {
            return DatabaseManager.PaintDatabase.GetPaintById(this.paintConfigId);
        }

        public int GetPaintConfigId() {
            return this.paintConfigId;
        }

        public int GetSharedMaterialIdx() {
            return this.sharedMaterialIdx;
        }

        public Color GetAdditionalColor() {
            return this.additionalColor;
        }

        public void SetAdditionalColor(Color color) {
            this.additionalColor = color;
        }

        public void SetPaintConfigId(int paintConfigId) {
            this.paintConfigId = paintConfigId;
        }
    }
}