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
        }

        [Client]
        public void PreviewMaterialOnFace(RaycastHit hit, PaintBucket paintBucket) {
            if (this.coverSettingsInPreview.Count == 0) {
                this.coverSettingsInPreview = new Dictionary<int, CoverSettings>(this.coverSettingsByFaces);
            }

            Mesh mesh = meshCollider.sharedMesh;

            int limit = hit.triangleIndex * 3;
            int submesh;
            for (submesh = 0; submesh < mesh.subMeshCount; submesh++) {
                int numIndices = mesh.GetTriangles(submesh).Length;
                if (numIndices > limit)
                    break;

                limit -= numIndices;
            }

            if (this.coverSettingsInPreview[submesh].Equals(paintBucket)) {
                this.coverSettingsInPreview[submesh] = this.coverSettingsByFaces[submesh];
            } else {
                this.coverSettingsInPreview[submesh] = new CoverSettings {paintConfigId = paintBucket.PaintConfigId, additionalColor = paintBucket.GetColor()};
            }

            this.UpdateWallFaces();
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
                CoverConfig coverConfig = DatabaseManager.PaintDatabase.GetPaintById(coverSettings.paintConfigId);
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