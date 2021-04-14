using System.Collections.Generic;
using Mirror;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class Wall : MonoBehaviour {
        [Header("Wall settings")]
        [Tooltip("Represent all id allowed to be modified")]
        [SerializeField]
        private int[] allowedSharedMaterialIds;

        private Dictionary<int, CoverSettings> coverSettingsByFaces = new Dictionary<int, CoverSettings>();
        
        private Dictionary<int, CoverSettings> coverSettingsInPreview = new Dictionary<int, CoverSettings>();

        private new MeshRenderer renderer;
        private MeshCollider meshCollider;

        private BoxCollider[] boxColliders;

        private void Awake() {
            this.renderer = GetComponent<MeshRenderer>();
            this.meshCollider = GetComponent<MeshCollider>();
            this.boxColliders = GetComponents<BoxCollider>();

            //this.EnableCollidersOfType(ColliderTypeEnum.BOX_COLLIDER);
        }

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

        public Material[] SharedMaterials() => this.renderer.sharedMaterials;

        /*public void EnableCollidersOfType(ColliderTypeEnum type) {
            foreach (BoxCollider boxCollider in this.boxColliders) {
                boxCollider.enabled = type == ColliderTypeEnum.BOX_COLLIDER;
            }

            this.meshCollider.enabled = type == ColliderTypeEnum.MESH_COLLIDER;
        }*/

        public void ApplyModification() {
            this.coverSettingsInPreview.Clear();
        }

        public bool IsPreview() {
            return this.coverSettingsInPreview.Count > 0;
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

            // Prevent to paint specific faces
            //if (!Enumerable.Contains(allowedSharedMaterialIds, submesh)) return;

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
                CoverSettings coverSettings = settingsToUse[i];
                PaintConfig paintConfig = DatabaseManager.PaintDatabase.GetPaintById(coverSettings.paintConfigId);
                Material materialToApply = new Material(paintConfig.GetMaterial());

                if (paintConfig.AllowCustomColor()) {
                    materialToApply.color = coverSettings.additionalColor;
                }

                sharedMaterials[i] = materialToApply;
            }

            this.renderer.sharedMaterials = sharedMaterials;
        }
    }
}