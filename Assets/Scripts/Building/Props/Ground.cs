using Mirror;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class Ground: MonoBehaviour {
        [Header("Ground settings")]
        [SerializeField]
        private CoverSettings currentCover = new CoverSettings {
            paintConfigId = 6,
            additionalColor = Color.white
        };

        private new Renderer renderer;

        private CoverSettings oldCoverSettings;

        private bool preview;

        private ApartmentController apartmentController;

        private void Awake() {
            this.renderer = GetComponent<Renderer>();
            this.apartmentController = GetComponentInParent<ApartmentController>();
        }

        public ApartmentController ApartmentController => apartmentController;

        [Client]
        public void Preview(CoverSettings settings) {
            if (this.preview) {
                this.ResetPreview();
            } else {
                this.oldCoverSettings = this.currentCover;
                this.SetCoverSettings(settings);
                this.preview = true;
            }
        }

        [Client]
        public void ApplyModification() {
            this.preview = false;
        }

        [Client]
        public void ResetPreview() {
            this.SetCoverSettings(this.oldCoverSettings);
            this.preview = false;
        }

        public void SetCoverSettings(CoverSettings settings) {
            this.currentCover = settings;
            this.ApplyPaint();
        }

        public bool IsPreview() {
            return this.preview;
        }
        
        private void ApplyPaint() {
            CoverConfig coverConfig = DatabaseManager.PaintDatabase.GetPaintById(this.currentCover.paintConfigId);

            if (coverConfig) {
                Material materialToApply = new Material(coverConfig.GetMaterial());

                if (coverConfig.AllowCustomColor()) {
                    materialToApply.color = this.currentCover.additionalColor;
                }

                this.renderer.material = materialToApply;
            }
        }

        public CoverSettings CurrentCover => currentCover;
    }
}