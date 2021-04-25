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
        public void Preview(PaintConfig paintConfig) {
            if (this.preview) {
                this.ResetPreview();
            } else {
                this.oldPaintConfigId = this.paintConfigId;
                this.PaintConfigId = paintConfig.GetId();
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
            PaintConfig paintConfig = DatabaseManager.PaintDatabase.GetPaintById(this.paintConfigId);

            if (paintConfig) {
                this.renderer.material = paintConfig.GetMaterial();
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