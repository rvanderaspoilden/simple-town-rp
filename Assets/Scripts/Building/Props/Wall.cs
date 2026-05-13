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
        }

        public Dictionary<int, CoverSettings> CoverSettingsInPreview => coverSettingsInPreview;

        public Dictionary<int, CoverSettings> CoverSettingsByFaces => coverSettingsByFaces;

        public Material[] SharedMaterials() => this.GetComponent<Renderer>().sharedMaterials;

        public void ApplyModification() {
            this.coverSettingsInPreview.Clear();
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
        }

        [Client]
        public void ErasePaintOnFace(int submesh) {
            if (submesh < 0) return;
            EnsurePreviewSeeded();
            if (this.coverSettingsByFaces.TryGetValue(submesh, out CoverSettings original)) {
                this.coverSettingsInPreview[submesh] = original;
                this.UpdateWallFaces();
            }
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
            }
            this.UpdateWallFaces();
        }

        private void EnsurePreviewSeeded() {
            if (this.coverSettingsInPreview.Count == 0) {
                this.coverSettingsInPreview = new Dictionary<int, CoverSettings>(this.coverSettingsByFaces);
            }
        }

        // ── Hover preview ────────────────────────────────────────────────────
        // A purely visual swap on a single face — does NOT touch coverSettingsInPreview,
        // so cancelling without painting won't trigger a network update.
        private int _hoverSubmesh = -1;
        private Material _hoverPrevMaterial;

        [Client]
        public void HoverFace(int submesh, PaintBucketBehaviour bucket) {
            if (_hoverSubmesh == submesh) return;
            ClearHover();
            if (submesh < 0) return;
            if (Array.IndexOf(this.sharedMaterialsToIgnore, submesh) != -1) return;

            if (this.renderer == null) this.renderer = GetComponent<MeshRenderer>();
            Material[] mats = this.renderer.sharedMaterials;
            if (submesh >= mats.Length) return;

            CoverConfig coverConfig = DatabaseManager.GetPaintById(bucket.PaintConfigId);
            if (coverConfig == null) return;

            _hoverPrevMaterial = mats[submesh];
            Material newMat = new Material(coverConfig.GetMaterial());
            if (coverConfig.AllowCustomColor()) newMat.color = bucket.GetColor();
            mats[submesh] = newMat;
            this.renderer.sharedMaterials = mats;
            _hoverSubmesh = submesh;
        }

        [Client]
        public void ClearHover() {
            if (_hoverSubmesh < 0) return;
            if (this.renderer == null) this.renderer = GetComponent<MeshRenderer>();
            Material[] mats = this.renderer.sharedMaterials;
            if (_hoverSubmesh < mats.Length) {
                mats[_hoverSubmesh] = _hoverPrevMaterial;
                this.renderer.sharedMaterials = mats;
            }
            _hoverSubmesh = -1;
            _hoverPrevMaterial = null;
        }

        /// <summary>Drop the hover bookkeeping without restoring the material — call when the face has just been painted.</summary>
        [Client]
        public void ConsumeHover() {
            _hoverSubmesh = -1;
            _hoverPrevMaterial = null;
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