using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sim.Building {
    public class FoundationRenderer : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private GameObject foundationObj;

        [SerializeField] private Material materialToApply;

        [SerializeField] private Renderer[] renderersToModify;

        [SerializeField] private bool interactWithCameraDistance;

        [Header("Only for debug")]
        [SerializeField] private bool showFoundation;

        [SerializeField] private Dictionary<Renderer, Material[]> defaultMaterialsByRenderer;

        private void Awake() {
            this.defaultMaterialsByRenderer = this.renderersToModify.ToList().ToDictionary(x => x, x => x.materials);
        }

        public void ShowFoundation(bool state) {
            if (this.showFoundation == state) { // Prevent useless treatments
                return;
            }
    
            this.showFoundation = state;
            this.UpdateGraphics();
        }

        public bool CanInteractWithCameraDistance() {
            return this.interactWithCameraDistance;
        }

        private void UpdateGraphics() {
            foreach (Renderer renderer in this.renderersToModify) {
                Material[] newMaterials = new Material[renderer.materials.Length];
                
                for (int i = 0; i < renderer.materials.Length; i++) {
                    newMaterials[i] = this.showFoundation ? this.materialToApply : this.defaultMaterialsByRenderer[renderer][i];
                }

                renderer.materials = newMaterials;
            }

            if (foundationObj) { // Optionnal
                this.foundationObj.SetActive(this.showFoundation);
            }
        }
    }
}