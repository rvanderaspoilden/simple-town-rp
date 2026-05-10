using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening.Core;
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
        private PropBehaviourBase currentProps;

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
            this.currentProps = GetComponent<PropBehaviourBase>();
            this.propsRenderer = GetComponent<PropsRenderer>();
            this.collider = GetComponent<Collider>();

            if (navMeshObstacle) {
                // disable this to avoid collision with player agent
                navMeshObstacle.enabled = false;
            }

            this.gameObject.layer = LayerMask.NameToLayer("Preview");

            // When BuildPreview is added retroactively to a prop already overlapping
            // trigger volumes (Edit on a placed prop), Unity does not always replay
            // OnTriggerEnter/Stay for those pre-existing contacts after the layer
            // change — `isInBuildableArea` would stay false forever and the preview
            // would be stuck red. Seed overlap state explicitly via OverlapBox.
            SeedOverlapState();
        }

        private void SeedOverlapState() {
            if (this.collider == null) return;

            Bounds b = this.collider.bounds;
            // Bounds is world-AABB; using identity rotation is conservative but fine
            // for seeding (we only need an initial snapshot — OnTriggerStay/Exit take
            // over from the next FixedUpdate).
            Collider[] hits = Physics.OverlapBox(
                b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

            int selfIgnored = 0;
            foreach (Collider other in hits) {
                if (other == this.collider || other.transform.IsChildOf(this.transform)) {
                    selfIgnored++;
                    continue;
                }
                RouteOverlap(other);
            }

            Debug.Log($"[BuildMode] Seeded preview prop={this.currentProps?.GetInstanceID()} " +
                      $"hits={hits.Length} selfIgnored={selfIgnored} " +
                      $"inBuildableArea={isInBuildableArea} blockers={colliderTriggered.Count}");
        }

        private void RouteOverlap(Collider other) {
            if (other.CompareTag("Buildable Area")) {
                if (this.buildableArea != other) {
                    this.buildableArea = other;
                    ApartmentController apt = this.buildableArea.GetComponentInParent<ApartmentController>();
                    this.isInBuildableArea = apt != null && apt.IsTenant(PlayerController.Local.CharacterData);
                }
                return;
            }

            if (other.CompareTag("Roof") || other.CompareTag("Dissonance") || other.CompareTag("Geographic Area")) return;

            if (this.currentProps.IsWallProps() && !this.colliderTriggered.Contains(other)) {
                this.colliderTriggered.Add(other);
            } else if (this.currentProps.IsGroundProps()
                       && other.gameObject.layer != LayerMask.NameToLayer("Ground")
                       && !this.colliderTriggered.Contains(other)) {
                this.colliderTriggered.Add(other);
            }
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

            Vector3 offset = this.transform.TransformDirection(new Vector3(0, ((BoxCollider)this.collider).center.y, 0));
            
            Vector3 upperLeftPos = this.transform.position + offset + this.transform.TransformDirection(new Vector3(-this.colliderBounds.x, this.colliderBounds.y, 0));
            Vector3 upperRightPos = this.transform.position + offset + this.transform.TransformDirection(new Vector3(this.colliderBounds.x, this.colliderBounds.y, 0));
            Vector3 lowerLeftPos = this.transform.position + offset + this.transform.TransformDirection(new Vector3(-this.colliderBounds.x, -this.colliderBounds.y, 0));
            Vector3 lowerRightPos = this.transform.position + offset + this.transform.TransformDirection(new Vector3(this.colliderBounds.x, -this.colliderBounds.y, 0));

            bool isUpperLeftValid = Physics.Raycast(upperLeftPos, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));
            bool isUpperRightValid = Physics.Raycast(upperRightPos, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));
            bool isLowerLeftValid = Physics.Raycast(lowerLeftPos, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));
            bool isLowerRightValid = Physics.Raycast(lowerRightPos, -this.transform.forward, Mathf.Abs(this.colliderBounds.z) + 0.1f, (1 << 12));

            return this.transform.rotation.eulerAngles != Vector3.zero && isUpperLeftValid && isUpperRightValid && isLowerRightValid && isLowerLeftValid;
        }

        private void OnTriggerStay(Collider other) {
            // Ignore self / our own children — guards against a prop's child colliders
            // ever reaching this callback if a future prop hierarchy adds them.
            if (other == this.collider || other.transform.IsChildOf(this.transform)) return;

            RouteOverlap(other);
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

            this.haveFreeArea = this.colliderTriggered.Count(x => x.gameObject.activeInHierarchy) == 0;
            bool wasPlaceable = this.placeable;
            this.placeable = this.haveFreeArea && this.detectGround && this.validRotation && this.isInBuildableArea;

            this.propsRenderer.SetPreviewState(this.placeable ? PreviewStateEnum.VALID : PreviewStateEnum.ERROR);

            if (wasPlaceable != this.placeable) {
                Debug.Log($"[BuildMode] Local placement valid={this.placeable} " +
                          $"freeArea={this.haveFreeArea} ground={this.detectGround} " +
                          $"rot={this.validRotation} inBuildable={this.isInBuildableArea} " +
                          $"blockers={this.colliderTriggered.Count}");
            }

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