using Mirror;
using Sim.Enums;
using Sim.Interactables;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class PaintBucket : Props {
        [Header("Bucket Settings")]
        [SyncVar]
        [SerializeField]
        private Color color = Color.white;

        [Header("Bucket settings debug")]
        [SyncVar]
        [SerializeField]
        private int paintConfigId;

        private CoverConfig coverConfig;
        public delegate void OnOpen(PaintBucket bucketOpened);

        public static event OnOpen OnOpened;

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.PAINT)) {
                OnOpened?.Invoke(this);
            }
        }

        [Server]
        public void Init(int paintId, float[] colorArray) {
            this.paintConfigId = paintId;
            
            if (colorArray != null && colorArray.Length >= 3) {
                this.color = new Color(colorArray[0], colorArray[1], colorArray[2]);
            }
        }

        public CoverConfig GetPaintConfig() {
            return DatabaseManager.PaintDatabase.GetPaintById(this.paintConfigId);
        }

        public CoverSettings GetCoverSettings() {
            return new CoverSettings {paintConfigId = this.PaintConfigId, additionalColor = this.GetColor()};
        }

        public int PaintConfigId => paintConfigId;
        
        public Color GetColor() {
            return this.color;
        }
    }
}