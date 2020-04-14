using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Sim.Building {
    public class BuildPreview : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private Material errorMaterial;

        [Header("Only for debug")]
        [SerializeField] private Renderer[] renderers;
        [SerializeField] private Material[] defaultRendererMaterials;
        [SerializeField] private NavMeshObstacle navMeshObstacle;
        [SerializeField] private bool placeable;

        [SerializeField] private List<Collider> colliderTriggered;

        private void Awake() {
            this.colliderTriggered = new List<Collider>();
            this.renderers = GetComponentsInChildren<Renderer>();
            this.defaultRendererMaterials = this.renderers.ToList().Select(x => x.material).ToArray();
            this.navMeshObstacle = GetComponentInChildren<NavMeshObstacle>();

            if (navMeshObstacle) { // disable this to avoid collision with player agent
                navMeshObstacle.enabled = false;
            }
        }

        private void OnTriggerStay(Collider other) {
            if (other.gameObject.layer != LayerMask.NameToLayer("Ground") && !this.colliderTriggered.Find(x => x == other)) {
                this.colliderTriggered.Add(other);
            }
            
            this.CheckValidity();
        }

        private void OnTriggerExit(Collider other) {
            this.colliderTriggered.Remove(other);
            this.CheckValidity();
        }

        private void CheckValidity() {
            this.placeable = this.colliderTriggered.Count == 0;

            if (this.placeable) {
                for (int i = 0; i < this.defaultRendererMaterials.Length; i++) {
                    this.renderers[i].material = this.defaultRendererMaterials[i];
                }
            } else {
                foreach (Renderer renderer in this.renderers) {
                    renderer.material = this.errorMaterial;
                }
            }
        }

        public void SetErrorMaterial(Material mat) {
            this.errorMaterial = mat;
        }

        public bool IsPlaceable() {
            return this.placeable;
        }
    }   
}
