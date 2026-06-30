using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class Wall : MonoBehaviour {
        [Header("Wall settings")]
        [Tooltip("Represent all id allowed to be modified")]
        [SerializeField]
        private int[] sharedMaterialsToIgnore;

        [SerializeField]
        private int[] sharedMaterialsToHide;

        private Dictionary<int, CoverSettings> coverSettingsByFaces = new Dictionary<int, CoverSettings>();

        private Dictionary<int, CoverSettings> coverSettingsInPreview = new Dictionary<int, CoverSettings>();

        private Dictionary<int, Material> sharedMaterialsOrigin = new Dictionary<int, Material>();

        private new MeshRenderer renderer;
        private MeshCollider meshCollider;

        private ApartmentController apartmentController;

        private void Awake() {
            this.renderer = GetComponent<MeshRenderer>();
            this.meshCollider = GetComponent<MeshCollider>();
            this.apartmentController = GetComponentInParent<ApartmentController>();

            this.sharedMaterialsOrigin = this.sharedMaterialsToIgnore.ToDictionary(x => x, x => this.renderer.sharedMaterials[x]);
        }

        public ApartmentController ApartmentController => apartmentController;

        public void Setup(Dictionary<int, CoverSettings> coverSettings) {
            this.coverSettingsByFaces = coverSettings;
            this.UpdateWallFaces();
        }

        [Client]
        public void Reset() {
            this.coverSettingsInPreview.Clear();

            this.UpdateWallFaces();

            this.meshCollider.enabled = true;

            ClearAllOutlines();
            _hoverPreviewSubmesh = -1;
            _hoverPreviewPrev = null;
        }

        public Dictionary<int, CoverSettings> CoverSettingsInPreview => coverSettingsInPreview;

        public Dictionary<int, CoverSettings> CoverSettingsByFaces => coverSettingsByFaces;

        public Material[] SharedMaterials() => this.GetComponent<Renderer>().sharedMaterials;

        public void ApplyModification() {
            this.coverSettingsInPreview.Clear();
            ClearAllOutlines();
            _hoverPreviewSubmesh = -1;
            _hoverPreviewPrev = null;
        }

        public bool IsPreview() {
            return this.coverSettingsInPreview.Count > 0;
        }

        public void HideWalls() {
            Material[] materials = this.renderer.sharedMaterials;
                
            foreach (var i in this.sharedMaterialsToHide) {
                materials[i] = DatabaseManager.Instance.GetTransparentMaterial();
            }

            this.renderer.sharedMaterials = materials;

            this.meshCollider.enabled = false;
        }

        /// <summary>
        /// Returns the submesh index hit by the raycast (-1 if invalid).
        /// </summary>
        public int GetSubmeshFromHit(RaycastHit hit) {
            Mesh mesh = meshCollider.sharedMesh;
            if (mesh == null || hit.triangleIndex < 0) return -1;

            int limit = hit.triangleIndex * 3;
            int submesh;
            for (submesh = 0; submesh < mesh.subMeshCount; submesh++) {
                int numIndices = mesh.GetTriangles(submesh).Length;
                if (numIndices > limit) return submesh;
                limit -= numIndices;
            }
            return -1;
        }

        /// <summary>True if the face at <paramref name="submesh"/> currently displays the bucket's paint settings.</summary>
        public bool IsFacePaintedWith(int submesh, PaintBucketBehaviour paintBucket) {
            if (submesh < 0) return false;
            Dictionary<int, CoverSettings> current = this.coverSettingsInPreview.Count > 0
                ? this.coverSettingsInPreview
                : this.coverSettingsByFaces;
            return current.TryGetValue(submesh, out CoverSettings cs) && cs.Equals(paintBucket);
        }

        /// <summary>True when the face's currently displayed cover differs from its persisted base value.</summary>
        public bool IsFacePainted(int submesh) {
            if (submesh < 0) return false;
            if (this.coverSettingsInPreview.Count == 0) return false; // no override → showing base
            if (!this.coverSettingsInPreview.TryGetValue(submesh, out var preview)) return false;
            if (!this.coverSettingsByFaces.TryGetValue(submesh, out var baseSettings)) return true;
            return preview.paintConfigId != baseSettings.paintConfigId
                || preview.additionalColor != baseSettings.additionalColor;
        }

        [Client]
        public void ApplyPaintOnFace(int submesh, PaintBucketBehaviour paintBucket) {
            if (submesh < 0) return;
            EnsurePreviewSeeded();
            this.coverSettingsInPreview[submesh] = new CoverSettings {
                paintConfigId   = paintBucket.PaintConfigId,
                additionalColor = paintBucket.GetColor()
            };
            this.UpdateWallFaces();
            AddOutline(submesh);
        }

        [Client]
        public void ErasePaintOnFace(int submesh) {
            if (submesh < 0) return;
            EnsurePreviewSeeded();
            if (this.coverSettingsByFaces.TryGetValue(submesh, out CoverSettings original)) {
                this.coverSettingsInPreview[submesh] = original;
                this.UpdateWallFaces();
            }
            RemoveOutline(submesh);
        }

        /// <summary>Apply the bucket's paint to every editable face on this wall.</summary>
        [Client]
        public void ApplyPaintOnAllFaces(PaintBucketBehaviour paintBucket) {
            EnsurePreviewSeeded();
            CoverSettings target = new CoverSettings {
                paintConfigId   = paintBucket.PaintConfigId,
                additionalColor = paintBucket.GetColor()
            };
            foreach (int i in this.coverSettingsByFaces.Keys.ToList()) {
                if (Array.IndexOf(this.sharedMaterialsToIgnore, i) != -1) continue;
                this.coverSettingsInPreview[i] = target;
                AddOutline(i);
            }
            this.UpdateWallFaces();
        }

        private void EnsurePreviewSeeded() {
            if (this.coverSettingsInPreview.Count == 0) {
                this.coverSettingsInPreview = new Dictionary<int, CoverSettings>(this.coverSettingsByFaces);
            }
        }

        // ── Hover & paint-preview outline ────────────────────────────────────
        // Two independent layers:
        //   1. Outlines : a PaintHoverOutline proxy per submesh that should display the white
        //      contour. The screen-space mask shader fuses several proxies into a single combined
        //      silhouette. A submesh receives an outline as soon as it is part of the paint
        //      preview (coverSettingsInPreview, drag), or while it is the lone hover preview
        //      (pre-click hover). Outlines for painted submeshes survive mouse release and only
        //      go away on validate/cancel/erase — that's the "what I painted" feedback.
        //   2. Hover preview material : a single submesh shows the bucket's material BEFORE any
        //      paint is committed. EndHoverPreview restores the saved material and disposes the
        //      face's outline IFF it isn't part of the committed paint preview.
        private readonly Dictionary<int, PaintHoverOutline> _outlines = new Dictionary<int, PaintHoverOutline>();
        private int _hoverPreviewSubmesh = -1;
        private Material _hoverPreviewPrev;

        /// <summary>Single-face hover: swap preview material on the submesh and outline it.
        /// Any previous hover preview is rolled back (its outline drops UNLESS the face is part
        /// of the committed paint preview).</summary>
        [Client]
        public void HoverFace(int submesh, PaintBucketBehaviour bucket) {
            if (_hoverPreviewSubmesh == submesh) return;
            EndHoverPreview();
            if (submesh < 0) return;
            if (Array.IndexOf(this.sharedMaterialsToIgnore, submesh) != -1) return;

            if (this.renderer == null) this.renderer = GetComponent<MeshRenderer>();
            Material[] mats = this.renderer.sharedMaterials;
            if (submesh >= mats.Length) return;

            CoverConfig coverConfig = DatabaseManager.GetPaintById(bucket.PaintConfigId);
            if (coverConfig == null) return;

            _hoverPreviewPrev = mats[submesh];
            Material newMat = new Material(coverConfig.GetMaterial());
            if (coverConfig.AllowCustomColor()) newMat.color = bucket.GetColor();
            mats[submesh] = newMat;
            this.renderer.sharedMaterials = mats;
            _hoverPreviewSubmesh = submesh;

            AddOutline(submesh);
        }

        /// <summary>Roll back the hover-preview material swap (if any). The outline survives if
        /// the face is part of the committed paint preview, otherwise it is removed.</summary>
        [Client]
        public void EndHoverPreview() {
            if (_hoverPreviewSubmesh < 0) return;
            int s = _hoverPreviewSubmesh;
            if (this.renderer == null) this.renderer = GetComponent<MeshRenderer>();
            Material[] mats = this.renderer.sharedMaterials;
            if (s < mats.Length) {
                mats[s] = _hoverPreviewPrev;
                this.renderer.sharedMaterials = mats;
            }
            _hoverPreviewSubmesh = -1;
            _hoverPreviewPrev = null;
            if (!IsFacePainted(s)) RemoveOutline(s);
        }

        /// <summary>Drop the hover-preview bookkeeping WITHOUT restoring the material — the caller
        /// is about to overwrite the material via the real paint apply. Outline is kept.</summary>
        [Client]
        public void ConsumeHover() {
            _hoverPreviewSubmesh = -1;
            _hoverPreviewPrev = null;
        }

        /// <summary>Ensure this submesh has an outline (idempotent). Use after a no-op paint
        /// click on an already-painted face so the selection contour catches up to state.</summary>
        [Client]
        public void OutlineFace(int submesh) {
            AddOutline(submesh);
        }

        /// <summary>Full reset: restore any hover material AND destroy every outline. Called on
        /// paint mode exit (validate/cancel/mode change) — never during a drag.</summary>
        [Client]
        public void ClearHover() {
            EndHoverPreview();
            ClearAllOutlines();
        }

        private void AddOutline(int submesh) {
            if (submesh < 0) return;
            if (Array.IndexOf(this.sharedMaterialsToIgnore, submesh) != -1) return;
            if (_outlines.ContainsKey(submesh)) return;
            Mesh source = this.meshCollider != null ? this.meshCollider.sharedMesh : null;
            PaintHoverOutline proxy = PaintHoverOutline.Build(this.transform, source, submesh);
            if (proxy != null) _outlines[submesh] = proxy;
        }

        private void RemoveOutline(int submesh) {
            if (_outlines.TryGetValue(submesh, out PaintHoverOutline p)) {
                if (p != null) p.Dispose();
                _outlines.Remove(submesh);
            }
        }

        private void ClearAllOutlines() {
            if (_outlines.Count == 0) return;
            foreach (PaintHoverOutline p in _outlines.Values) if (p != null) p.Dispose();
            _outlines.Clear();
        }

        public void UpdateWallFaces() {
            if (this.renderer == null) {
                this.renderer = GetComponent<MeshRenderer>();
            }

            Dictionary<int, CoverSettings> settingsToUse = this.coverSettingsInPreview.Count > 0 ? this.coverSettingsInPreview : this.coverSettingsByFaces;
            Material[] sharedMaterials = this.renderer.sharedMaterials;

            for (int i = 0; i < settingsToUse.Count; i++) {
                if (Array.IndexOf(this.sharedMaterialsToIgnore, i) != -1) {
                    sharedMaterials[i] = this.sharedMaterialsOrigin[i];
                    continue;
                }
                
                CoverSettings coverSettings = settingsToUse[i];
                CoverConfig coverConfig = DatabaseManager.GetPaintById(coverSettings.paintConfigId);
                Material materialToApply = new Material(coverConfig.GetMaterial());

                if (coverConfig.AllowCustomColor()) {
                    materialToApply.color = coverSettings.additionalColor;
                }

                sharedMaterials[i] = materialToApply;
            }

            this.renderer.sharedMaterials = sharedMaterials;
        }
    }
}