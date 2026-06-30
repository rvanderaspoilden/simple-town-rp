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
            EnsureOutline();
        }

        /// <summary>Reverts to the pre-preview settings, no-op if not in preview. Drops the outline
        /// since the ground is back to its committed state.</summary>
        [Client]
        public void ErasePaint() {
            if (this.preview) this.ResetPreview();
            DisposeOutline();
        }

        // ── Hover & paint-preview outline ────────────────────────────────────
        // The ground has a single face, so the outline is a single proxy. A ground gets an outline
        // as soon as it's part of the committed paint preview (this.preview) — that outline lives
        // until validate/cancel/erase. While the player is purely hovering (no click), the outline
        // also shows; EndHoverPreview drops it only if the ground is NOT in committed preview.
        private bool _isHovering;
        private PaintHoverOutline _outline;

        [Client]
        public void HoverApply(CoverSettings settings) {
            CoverConfig coverConfig = DatabaseManager.GetPaintById(settings.paintConfigId);
            if (coverConfig == null) return;
            Material mat = new Material(coverConfig.GetMaterial());
            if (coverConfig.AllowCustomColor()) mat.color = settings.additionalColor;
            this.renderer.material = mat;
            EnsureOutline();
            _isHovering = true;
        }

        /// <summary>Roll back the hover-preview material swap. Outline survives if the ground is
        /// part of the committed paint preview, otherwise it is dropped.</summary>
        [Client]
        public void EndHoverPreview() {
            if (!_isHovering) return;
            _isHovering = false;
            this.ApplyPaint(); // re-render from real state (currentCover)
            if (!this.preview) DisposeOutline();
        }

        /// <summary>Drop the hover-preview bookkeeping WITHOUT re-rendering — the caller is about
        /// to apply the real paint. Outline stays.</summary>
        [Client]
        public void ConsumeHover() {
            _isHovering = false;
        }

        /// <summary>Full reset: roll back any hover material AND drop the outline. Called on paint
        /// mode exit (validate/cancel/mode change) — never during a drag.</summary>
        [Client]
        public void ClearHover() {
            EndHoverPreview();
            DisposeOutline();
        }

        public void EnsureOutline() {
            if (_outline != null) return;
            MeshFilter mf = GetComponent<MeshFilter>();
            Mesh source = mf != null ? mf.sharedMesh : null;
            _outline = PaintHoverOutline.Build(this.transform, source);
        }

        private void DisposeOutline() {
            if (_outline != null) { _outline.Dispose(); _outline = null; }
        }

        [Client]
        public void ApplyModification() {
            this.preview = false;
            DisposeOutline();
            _isHovering = false;
        }

        [Client]
        public void ResetPreview() {
            this.SetCoverSettings(this.oldCoverSettings);
            this.preview = false;
            DisposeOutline();
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