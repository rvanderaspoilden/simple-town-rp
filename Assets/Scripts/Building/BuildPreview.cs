using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Enums;
using UnityEngine;
using UnityEngine.AI;

namespace Sim.Building {
    public class BuildPreview : MonoBehaviour {
        [Header("Only for debug")]
        [SerializeField]
        private NavMeshObstacle navMeshObstacle;

        [SerializeField]
        private bool haveFreeArea;

        [SerializeField]
        private bool detectGround;

        [SerializeField]
        private bool validRotation;

        [SerializeField]
        private bool isInBuildableArea;

        [SerializeField]
        private Props currentProps;

        [SerializeField]
        private bool placeable;

        [SerializeField]
        private new Collider collider;

        [SerializeField]
        private Vector3 colliderBounds;

        [SerializeField]
        private List<Collider> colliderTriggered;

        [SerializeField]
        private Collider buildableArea;

        private PropsRenderer propsRenderer;

        public delegate void PlaceableState(bool isPlaceable);

        public static event PlaceableState OnPlaceableStateChanged;

        private void Awake() {
            this.colliderTriggered = new List<Collider>();
            this.navMeshObstacle = GetComponentInChildren<NavMeshObstacle>();
            this.currentProps = GetComponent<Props>();
            this.propsRenderer = GetComponent<PropsRenderer>();
            this.collider = GetComponent<Collider>();

            if (navMeshObstacle) {
                // disable this to avoid collision with player agent
                navMeshObstacle.enabled = false;
            }

            this.gameObject.layer = LayerMask.NameToLayer("Preview");
        }

        private void Update() {
            if (Physics.Raycast(this.transform.position, Vector3.down, 10, (1 << 9))) {
                this.detectGround = this.CheckConnectedToWallConstraint();
            } else {
                this.detectGround = false;
            }

            if (this.currentProps.IsWallProps()) {
                this.validRotation = this.CheckWallPropsIntegrity();
            } else {
                this.validRotation = true;
            }

            this.CheckValidity();
        }

        /**
         * Methods which check if props is well connected to wall if property is checked in configuration
         * Return true if it's valid
         */
        private bool CheckConnectedToWallConstraint() {
            if (!this.currentProps.GetConfiguration().NeedToBeConnectedToWall()) {
                return true;
            }

            this.colliderBounds = this.transform.InverseTransformDirection(this.collider.bounds.extents);
            return Physics.Raycast(this.transform.position, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));
        }

        /**
         * This method is used to check if the props surface is totally on a wall face 
         */
        private bool CheckWallPropsIntegrity() {
            this.colliderBounds = this.transform.InverseTransformDirection(this.collider.bounds.extents);

            Vector3 upperLeftPos = this.transform.position + this.transform.TransformDirection(new Vector3(-this.colliderBounds.x, this.colliderBounds.y, 0));
            Vector3 upperRightPos = this.transform.position + this.transform.TransformDirection(new Vector3(this.colliderBounds.x, this.colliderBounds.y, 0));
            Vector3 lowerLeftPos = this.transform.position + this.transform.TransformDirection(new Vector3(-this.colliderBounds.x, -this.colliderBounds.y, 0));
            Vector3 lowerRightPos = this.transform.position + this.transform.TransformDirection(new Vector3(this.colliderBounds.x, -this.colliderBounds.y, 0));

            bool isUpperLeftValid = Physics.Raycast(upperLeftPos, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));
            bool isUpperRightValid = Physics.Raycast(upperRightPos, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));
            bool isLowerLeftValid = Physics.Raycast(lowerLeftPos, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));
            bool isLowerRightValid = Physics.Raycast(lowerRightPos, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));

            return this.transform.rotation.eulerAngles != Vector3.zero && isUpperLeftValid && isUpperRightValid && isLowerRightValid && isLowerLeftValid;
        }

        private void OnTriggerStay(Collider other) {
            if (other.CompareTag("Buildable Area") && this.buildableArea != other) {
                this.buildableArea = other;
                this.isInBuildableArea = this.buildableArea.GetComponentInParent<ApartmentController>().IsTenant(PlayerController.Local.CharacterData);
            } else if(!other.CompareTag("Buildable Area") && !other.CompareTag("Roof") && !other.CompareTag("Dissonance") && !other.CompareTag("Geographic Area")){
                if (this.currentProps.IsWallProps() && !this.colliderTriggered.Find(x => x == other)) {
                    this.colliderTriggered.Add(other);
                } else if (this.currentProps.IsGroundProps() && other.gameObject.layer != LayerMask.NameToLayer("Ground") &&
                           !this.colliderTriggered.Find(x => x == other)) {
                    this.colliderTriggered.Add(other);
                }
            }

            this.CheckValidity();
        }

        private void OnTriggerExit(Collider other) {
            if (buildableArea == other) {
                buildableArea = null;
                this.isInBuildableArea = false;
            }

            this.colliderTriggered.Remove(other);
            this.CheckValidity();
        }

        private void CheckValidity() {
            if (navMeshObstacle && navMeshObstacle.enabled) return;
                
            this.haveFreeArea = this.colliderTriggered.Count == 0;
            this.placeable = this.haveFreeArea && this.detectGround && this.validRotation && this.isInBuildableArea;

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