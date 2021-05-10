using Mirror;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class Ground: MonoBehaviour {
        [Header("Ground settings")]
        [SerializeField]
        private int paintConfigId;

        private new Renderer renderer;

        private int oldPaintConfigId;

        private bool preview;

        private ApartmentController apartmentController;

        private void Awake() {
            this.renderer = GetComponent<Renderer>();
            this.apartmentController = GetComponentInParent<ApartmentController>();
        }

        public ApartmentController ApartmentController => apartmentController;

        [Client]
        public void Preview(CoverConfig coverConfig) {
            if (this.preview) {
                this.ResetPreview();
            } else {
                this.oldPaintConfigId = this.paintConfigId;
                this.PaintConfigId = coverConfig.GetId();
                this.preview = true;
            }
        }

        [Client]
        public void ApplyModification() {
            this.preview = false;
        }

        [Client]
        public void ResetPreview() {
            this.PaintConfigId = this.oldPaintConfigId;
            this.preview = false;
        }

        public int PaintConfigId {
            get => paintConfigId;
            set {
                paintConfigId = value;
                this.ApplyPaint();
            }
        }

        public bool IsPreview() {
            return this.preview;
        }
        
        private void ApplyPaint() {
            CoverConfig coverConfig = DatabaseManager.PaintDatabase.GetPaintById(this.paintConfigId);

            if (coverConfig) {
                this.renderer.material = coverConfig.GetMaterial();
            }
        }

        public CoverSettings CoverSettings() {
            return new CoverSettings {
                paintConfigId = paintConfigId,
                additionalColor = Color.white
            };
        }
    }
}