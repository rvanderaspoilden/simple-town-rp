using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Enums;
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
        [SerializeField] private bool haveFreeArea;
        [SerializeField] private bool detectGround;
        [SerializeField] private bool validRotation;
        [SerializeField] private Props currentProps;
        [SerializeField] private bool placeable;

        [SerializeField] private List<Collider> colliderTriggered;
        
        public delegate void OnPlaceableState(bool isPlaceable);

        public static event OnPlaceableState OnPlaceableStateChanged;

        private void Awake() {
            this.colliderTriggered = new List<Collider>();
            this.renderers = GetComponentsInChildren<Renderer>();
            this.defaultRendererMaterials = this.renderers.ToList().Select(x => x.material).ToArray();
            this.navMeshObstacle = GetComponentInChildren<NavMeshObstacle>();
            this.currentProps = GetComponent<Props>();

            if (navMeshObstacle) { // disable this to avoid collision with player agent
                navMeshObstacle.enabled = false;
            }

            this.gameObject.layer = LayerMask.NameToLayer("Preview");
        }

        private void Update() {
            if (Physics.Raycast(this.transform.position, Vector3.down, 10, (1 << 9))) {
                this.detectGround = true;
            } else {
                this.detectGround = false;
            }

            if (this.currentProps.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.WALL) {
                this.validRotation = this.transform.rotation.eulerAngles != Vector3.zero;
            } else {
                this.validRotation = true;
            }

            this.CheckValidity();
        }

        private void OnTriggerStay(Collider other) {
            if (this.currentProps.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.WALL && !this.colliderTriggered.Find(x => x == other)) {
                this.colliderTriggered.Add(other);
            } else if (this.currentProps.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND && other.gameObject.layer != LayerMask.NameToLayer("Ground") && !this.colliderTriggered.Find(x => x == other)) {
                this.colliderTriggered.Add(other);
            }

            this.CheckValidity();
        }

        private void OnTriggerExit(Collider other) {
            this.colliderTriggered.Remove(other);
            this.CheckValidity();
        }

        private void CheckValidity() {
            this.haveFreeArea = this.colliderTriggered.Count == 0;
            this.placeable = this.haveFreeArea && this.detectGround && this.validRotation;

            if (this.placeable) {
                for (int i = 0; i < this.defaultRendererMaterials.Length; i++) {
                    this.renderers[i].material = this.defaultRendererMaterials[i];
                }
            } else {
                foreach (Renderer renderer in this.renderers) {
                    renderer.material = this.errorMaterial;
                }
            }
            
            OnPlaceableStateChanged?.Invoke(this.placeable);
        }

        public void Destroy() {
            if (navMeshObstacle) {
                navMeshObstacle.enabled = true;
            }

            // reset materials
            for (int i = 0; i < this.defaultRendererMaterials.Length; i++) {
                this.renderers[i].material = this.defaultRendererMaterials[i];
            }

            this.gameObject.layer = LayerMask.NameToLayer("Props");

            Destroy(this);
        }

        public void SetErrorMaterial(Material mat) {
            this.errorMaterial = mat;
        }

        public bool IsPlaceable() {
            return this.placeable;
        }
    }
}