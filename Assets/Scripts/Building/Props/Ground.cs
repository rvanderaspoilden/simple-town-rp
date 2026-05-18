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

        /// <summary>True if this ground is currently displaying the bucket's paint settings.</summary>
        public bool IsPaintedWith(PaintBucketBehaviour paintBucket) {
            return this.currentCover.paintConfigId == paintBucket.PaintConfigId
                && this.currentCover.additionalColor == paintBucket.GetColor();
        }

        /// <summary>Idempotent apply — sets the cover to <paramref name="settings"/> and enters preview if not already.</summary>
        [Client]
        public void ApplyPaint(CoverSettings settings) {
            if (!this.preview) {
                this.oldCoverSettings = this.currentCover;
                this.preview = true;
            }
            this.SetCoverSettings(settings);
        }

        /// <summary>Reverts to the pre-preview settings, no-op if not in preview.</summary>
        [Client]
        public void ErasePaint() {
            if (this.preview) this.ResetPreview();
        }

        // ── Hover preview ────────────────────────────────────────────────────
        // Visual-only swap; does NOT modify currentCover so cancelling without painting
        // leaves no trace in the ground's state.
        private bool     _isHovering;

        [Client]
        public void HoverApply(CoverSettings settings) {
            CoverConfig coverConfig = DatabaseManager.GetPaintById(settings.paintConfigId);
            if (coverConfig == null) return;
            Material mat = new Material(coverConfig.GetMaterial());
            if (coverConfig.AllowCustomColor()) mat.color = settings.additionalColor;
            this.renderer.material = mat;
            _isHovering = true;
        }

        [Client]
        public void ClearHover() {
            if (!_isHovering) return;
            _isHovering = false;
            this.ApplyPaint(); // re-render from real state (currentCover)
        }

        /// <summary>Drop the hover flag without re-rendering — call when the ground has just been painted.</summary>
        [Client]
        public void ConsumeHover() {
            _isHovering = false;
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
            CoverConfig coverConfig = DatabaseManager.GetPaintById(this.currentCover.paintConfigId);

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