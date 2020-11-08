using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Enums;
using UnityEngine;

namespace Sim.Building {
    public class FoundationRenderer : MonoBehaviour {
        [Header("Settings")] [SerializeField] private GameObject foundationObj;

        [SerializeField] private Renderer[] renderersToModify;

        [SerializeField] private bool interactWithCameraDistance;

        [Header("Only for debug")] [SerializeField]
        private bool showFoundation;

        [SerializeField] private Props props;

        [SerializeField] private FoundationVisibilityEnum visibility = FoundationVisibilityEnum.AUTO;

        [SerializeField] private Dictionary<Renderer, Material[]> defaultMaterialsByRenderer;

        private void Awake() {
            this.props = GetComponent<Props>();
            this.SetupDefaultMaterials();
            this.SetVisibilityMode(FoundationVisibilityEnum.AUTO);
        }

        public void ShowFoundation(bool state) {
            this.showFoundation = state;

            this.UpdateGraphics();
        }

        public void SetupDefaultMaterials() {
            this.defaultMaterialsByRenderer = this.renderersToModify.ToList().ToDictionary(x => x, x => x.materials);
        }

        public bool CanInteractWithCameraDistance() {
            return this.interactWithCameraDistance;
        }

        public void SetVisibilityMode(FoundationVisibilityEnum visibility) {
            this.visibility = visibility;

            this.UpdateGraphics();
        }

        private void UpdateGraphics() {
            bool hide = false;

            if (this.visibility == FoundationVisibilityEnum.AUTO) {
                hide = this.showFoundation;
            }
            else if (visibility == FoundationVisibilityEnum.FORCE_HIDE) {
                hide = true;
            }
            else if (visibility == FoundationVisibilityEnum.FORCE_SHOW) {
                hide = false;
            }

            foreach (Renderer renderer in this.renderersToModify) {
                Material[] newMaterials = new Material[renderer.materials.Length];

                for (int i = 0; i < renderer.materials.Length; i++) {
                    if (hide) {
                        newMaterials[i] = DatabaseManager.Instance.GetTransparentMaterial();
                    }
                    else if (this.props && !this.props.IsBuilt()) {
                        newMaterials[i] = DatabaseManager.Instance.GetUnbuiltMaterial();
                    }
                    else {
                        newMaterials[i] = this.defaultMaterialsByRenderer[renderer][i];
                    }
                }

                renderer.materials = newMaterials;
            }

            if (foundationObj) {
                // Optional
                this.foundationObj.SetActive(hide);
            }
        }
    }
}