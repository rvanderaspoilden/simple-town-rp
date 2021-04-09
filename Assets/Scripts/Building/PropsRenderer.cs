using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Enums;
using UnityEngine;

namespace Sim.Building {
    public class PropsRenderer : MonoBehaviour {
        [Header("Settings")]
        [Tooltip("Set all renderers which can be impacted by material changes")]
        [SerializeField]
        private Renderer[] renderersToModify;
        
        [Tooltip("Mesh renderers will be impacted by primary style of selected preset in configuration")]
        [SerializeField]
        private MeshRenderer[] primaryStyleMeshRenderers;
        
        [Tooltip("Mesh renderers will be impacted by secondary style of selected preset in configuration")]
        [SerializeField]
        private MeshRenderer[] secondaryStyleMeshRenderers;
        
        [Tooltip("Mesh renderers will be impacted by tertiary style of selected preset in configuration")]
        [SerializeField]
        private MeshRenderer[] tertiaryStyleMeshRenderers;

        [Tooltip("Is props hideable ??")]
        [SerializeField]
        private bool hideable = true;

        [Tooltip("Set to true means the props will be hide if it's between target and camera view")]
        [SerializeField]
        private bool interactWithCameraDistance;

        [Tooltip("Small representation of the props when interactWithCameraDistance is set to true")]
        [SerializeField]
        private GameObject foundationObj;

        private VisibilityStateEnum state = VisibilityStateEnum.SHOW;
        private VisibilityModeEnum mode = VisibilityModeEnum.AUTO;
        private PreviewStateEnum previewState = PreviewStateEnum.NONE;
        private Dictionary<Renderer, Material[]> defaultMaterialsByRenderer;
        private Props props;

        private void Awake() {
            this.props = GetComponent<Props>();
            this.SetupDefaultMaterials();
        }

        private void Start() {
            this.SetVisibilityMode(VisibilityModeEnum.AUTO);
        }

        public void SetState(VisibilityStateEnum state) {
            this.state = state == VisibilityStateEnum.HIDE && this.hideable ? VisibilityStateEnum.HIDE : VisibilityStateEnum.SHOW;

            this.UpdateGraphics();
        }

        public void SetPreset(PropsPreset preset) {
            this.TryToApplyStyle(preset.Primary, this.primaryStyleMeshRenderers);
            this.TryToApplyStyle(preset.Secondary, this.secondaryStyleMeshRenderers);
            this.TryToApplyStyle(preset.Tertiary, this.tertiaryStyleMeshRenderers);
            
            this.UpdateGraphics();
        }

        private bool TryToApplyStyle(PropsStyle propsStyle, IEnumerable<MeshRenderer> meshRenderers) {
            if (!propsStyle.Enabled) return false;
            
            foreach (var meshRenderer in meshRenderers) {
                Material[] materialsToModify = this.defaultMaterialsByRenderer[meshRenderer];
                
                foreach (var material in materialsToModify) {
                    if (propsStyle.Material) {
                        material.CopyPropertiesFromMaterial(propsStyle.Material);
                    }

                    material.color = propsStyle.Color;
                }
            }

            return true;
        }

        /**
         * Keep all initial materials of the props to reset it at any moment
         */
        public void SetupDefaultMaterials() {
            this.defaultMaterialsByRenderer = this.renderersToModify.ToList().ToDictionary(x => x, x => x.materials);
        }

        public bool CanInteractWithCameraDistance() {
            return this.interactWithCameraDistance;
        }

        public bool IsHideable() {
            return this.hideable;
        }

        public void SetVisibilityMode(VisibilityModeEnum mode) {
            this.mode = mode;

            this.UpdateGraphics();
        }

        public void SetPreviewState(PreviewStateEnum previewState) {
            this.previewState = previewState;
            this.UpdateGraphics();
        }

        public void UpdateGraphics() {
            VisibilityStateEnum visibility = this.state;

            if (this.mode == VisibilityModeEnum.FORCE_HIDE && this.hideable) {
                visibility = VisibilityStateEnum.HIDE;
            } else if (this.mode == VisibilityModeEnum.FORCE_SHOW) {
                visibility = VisibilityStateEnum.SHOW;
            }

            if (this.defaultMaterialsByRenderer == null) {
                this.SetupDefaultMaterials();
            }


            foreach (Renderer renderer in this.renderersToModify) {
                Material[] newMaterials = new Material[renderer.materials.Length];

                for (int i = 0; i < renderer.materials.Length; i++) {
                    if (this.previewState == PreviewStateEnum.NONE) {
                        if (visibility == VisibilityStateEnum.HIDE) {
                            newMaterials[i] = DatabaseManager.Instance.GetTransparentMaterial();
                        } else if (visibility == VisibilityStateEnum.SHOW) {
                            if (this.props && !this.props.IsBuilt()) {
                                newMaterials[i] = DatabaseManager.Instance.GetUnbuiltMaterial();
                            } else {
                                newMaterials[i] = this.defaultMaterialsByRenderer[renderer][i];
                            }
                        }
                    } else {
                        if (this.previewState == PreviewStateEnum.ERROR) {
                            newMaterials[i] = DatabaseManager.Instance.GetErrorMaterial();
                        } else {
                            newMaterials[i] = this.defaultMaterialsByRenderer[renderer][i];
                        }
                    }
                }

                renderer.materials = newMaterials;
            }

            // Optional
            if (foundationObj) {
                this.foundationObj.SetActive(visibility == VisibilityStateEnum.HIDE);
            }
        }
    }
}