using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sim.Building {
    public class FoundationRenderer : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private GameObject foundationObj;

        [SerializeField] private Renderer[] renderersToModify;

        [SerializeField] private bool interactWithCameraDistance;

        [Header("Only for debug")]
        [SerializeField] private bool showFoundation;

        [SerializeField] private Props props;

        [SerializeField] private bool hideForced;

        [SerializeField] private Dictionary<Renderer, Material[]> defaultMaterialsByRenderer;
        
        private void Awake() {
            this.props = GetComponent<Props>();
            this.SetupDefaultMaterials();
        }
        
        public void ShowFoundation(bool state, bool forcedAction = false) {
            if ((this.showFoundation == state && !forcedAction) || (this.hideForced && !forcedAction) || (this.props && !this.props.IsBuilt())) { // Prevent useless treatments except if forced action
                return;
            }

            if (forcedAction) {
                this.hideForced = state;
            }

            this.showFoundation = state;
            this.UpdateGraphics();
        }

        public void SetupDefaultMaterials() {
            this.defaultMaterialsByRenderer = this.renderersToModify.ToList().ToDictionary(x => x, x => x.materials);
        }

        public bool CanInteractWithCameraDistance() {
            return this.interactWithCameraDistance;
        }

        public bool IsForceHidden() {
            return this.hideForced;
        }

        private void UpdateGraphics() {
            foreach (Renderer renderer in this.renderersToModify) {
                Material[] newMaterials = new Material[renderer.materials.Length];
                
                for (int i = 0; i < renderer.materials.Length; i++) {
                    newMaterials[i] = this.showFoundation ? DatabaseManager.Instance.GetTransparentMaterial() : this.defaultMaterialsByRenderer[renderer][i];
                }

                renderer.materials = newMaterials;
            }

            if (foundationObj) { // Optionnal
                this.foundationObj.SetActive(this.showFoundation);
            }
        }
    }
}