using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Enums;
using UnityEngine;
using UnityEngine.AI;

namespace Sim.Building {
    public class BuildPreview : MonoBehaviour {
        [Header("Only for debug")]
        [SerializeField] private NavMeshObstacle navMeshObstacle;
        [SerializeField] private bool haveFreeArea;
        [SerializeField] private bool detectGround;
        [SerializeField] private bool validRotation;
        [SerializeField] private Props currentProps;
        [SerializeField] private bool placeable;

        [SerializeField] private List<Collider> colliderTriggered;

        private PropsRenderer propsRenderer;
        
        public delegate void PlaceableState(bool isPlaceable);

        public static event PlaceableState OnPlaceableStateChanged;

        private void Awake() {
            this.colliderTriggered = new List<Collider>();
            this.navMeshObstacle = GetComponentInChildren<NavMeshObstacle>();
            this.currentProps = GetComponent<Props>();
            this.propsRenderer = GetComponent<PropsRenderer>();

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

            if (this.currentProps.IsWallProps()) {
                this.validRotation = this.transform.rotation.eulerAngles != Vector3.zero;
            } else {
                this.validRotation = true;
            }

            this.CheckValidity();
        }

        private void OnTriggerStay(Collider other) {
            if (this.currentProps.IsWallProps() && !this.colliderTriggered.Find(x => x == other)) {
                this.colliderTriggered.Add(other);
            } else if (this.currentProps.IsGroundProps() && other.gameObject.layer != LayerMask.NameToLayer("Ground") && !this.colliderTriggered.Find(x => x == other)) {
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

            this.propsRenderer.SetPreviewState(this.placeable ? PreviewStateEnum.VALID : PreviewStateEnum.ERROR);

            OnPlaceableStateChanged?.Invoke(this.placeable);
        }

        public void Destroy() {
            if (navMeshObstacle) {
                navMeshObstacle.enabled = true;
            }

            this.propsRenderer.SetPreviewState(PreviewStateEnum.NONE);

            this.gameObject.layer = LayerMask.NameToLayer("Props");

            Destroy(this);
        }

        public bool IsPlaceable() {
            return this.placeable;
        }
    }
}