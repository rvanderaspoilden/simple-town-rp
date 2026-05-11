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

        // Original collider trigger state, restored on Destroy().
        // BuildPreview relies on OnTriggerStay/Exit; if the prop's collider is solid
        // (isTrigger=false), no trigger callbacks fire and overlap state never updates.
        private bool restoreColliderIsTrigger;
        private bool colliderWasTrigger;

        // Trigger callbacks (OnTriggerStay/Exit) only fire when at least one of the
        // two colliders' GameObjects has a Rigidbody. Props ship without one, so we
        // add a kinematic Rigidbody for the duration of the preview and remove it
        // when the preview ends.
        private Rigidbody addedKinematicRigidbody;

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

            // Force the prop's collider into trigger mode for the duration of the preview
            // so OnTriggerStay/Exit fire reliably (props can now ship with isTrigger=false).
            if (this.collider != null && !this.collider.isTrigger) {
                this.colliderWasTrigger    = this.collider.isTrigger;
                this.restoreColliderIsTrigger = true;
                this.collider.isTrigger    = true;
            }

            // Ensure a Rigidbody is present so trigger callbacks fire against static
            // colliders (other props, walls, ground). Use kinematic so physics doesn't
            // move the prop while the user is dragging it.
            if (GetComponent<Rigidbody>() == null) {
                this.addedKinematicRigidbody = this.gameObject.AddComponent<Rigidbody>();
                this.addedKinematicRigidbody.isKinematic = true;
                this.addedKinematicRigidbody.useGravity  = false;
            }

            Debug.Log($"[BuildPreview/Awake] prop={this.currentProps?.name} " +
                      $"collider={this.collider?.GetType().Name} isTrigger={this.collider?.isTrigger} " +
                      $"hadRigidbody={this.addedKinematicRigidbody == null} " +
                      $"pos={this.transform.position} rot={this.transform.eulerAngles}");

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

            // Use the actual oriented box of the collider when possible (BoxCollider).
            // Previously this used world-AABB extents with identity rotation, which
            // over-includes for rotated props: a wall touched only by the AABB but
            // not by the real OBB would be seeded as a blocker, and OnTriggerExit
            // would never fire (the physics system never saw a real overlap).
            Vector3 center;
            Vector3 halfExtents;
            Quaternion rotation;

            if (this.collider is BoxCollider box) {
                center      = transform.TransformPoint(box.center);
                halfExtents = Vector3.Scale(box.size * 0.5f, transform.lossyScale);
                // Account for the box collider's own rotation contribution by using the
                // GO rotation (BoxCollider has no rotation of its own beyond the transform).
                rotation    = transform.rotation;
            } else {
                Bounds b    = this.collider.bounds;
                center      = b.center;
                halfExtents = b.extents;
                rotation    = Quaternion.identity;
            }

            Collider[] hits = Physics.OverlapBox(
                center, halfExtents, rotation, ~0, QueryTriggerInteraction.Collide);

            int selfIgnored = 0;
            foreach (Collider other in hits) {
                if (other == this.collider || other.transform.IsChildOf(this.transform)) {
                    selfIgnored++;
                    Debug.Log($"[BuildPreview/Seed] SKIP self/child {other.name}");
                    continue;
                }

                bool penetrating = Physics.ComputePenetration(
                    this.collider, this.transform.position, this.transform.rotation,
                    other,          other.transform.position, other.transform.rotation,
                    out _, out _);

                Debug.Log($"[BuildPreview/Seed] hit name={other.name} layer={LayerMask.LayerToName(other.gameObject.layer)} " +
                          $"tag={other.tag} isTrigger={other.isTrigger} type={other.GetType().Name} " +
                          $"penetrating={penetrating}");

                // Only seed colliders that actually penetrate the prop's OBB. This is
                // what the physics system would have raised an OnTriggerEnter for — if
                // there's no real penetration, OnTriggerExit will never fire either,
                // and the entry would stick in colliderTriggered forever.
                if (!penetrating) continue;

                RouteOverlap(other);
            }

            Debug.Log($"[BuildPreview/Seed] result prop={this.currentProps?.name} " +
                      $"hits={hits.Length} selfIgnored={selfIgnored} " +
                      $"inBuildableArea={isInBuildableArea} blockers={colliderTriggered.Count} " +
                      $"blockerNames=[{string.Join(",", colliderTriggered.Select(c => c?.name ?? "null"))}]");
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

        private float _lastBlockerDumpTime;

        private void FixedUpdate() {
            // Validate that each tracked blocker is still actually penetrating the prop.
            // The physics broadphase trigger-tracking and Physics.ComputePenetration don't
            // always agree (especially with concave MeshColliders): OverlapBox / ComputePen
            // may report a contact that Unity's trigger system never registers, leaving
            // stale entries that no OnTriggerExit will ever clean up.
            if (this.collider == null || this.colliderTriggered.Count == 0) return;

            for (int i = this.colliderTriggered.Count - 1; i >= 0; i--) {
                Collider other = this.colliderTriggered[i];
                if (other == null) { this.colliderTriggered.RemoveAt(i); continue; }
                if (!other.gameObject.activeInHierarchy) continue;

                bool stillOverlapping = Physics.ComputePenetration(
                    this.collider, this.transform.position, this.transform.rotation,
                    other,          other.transform.position, other.transform.rotation,
                    out _, out _);
                if (!stillOverlapping) {
                    Debug.Log($"[BuildPreview/PruneStale] removing {other.name} (no longer penetrating)");
                    this.colliderTriggered.RemoveAt(i);
                }
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

            // Periodic dump while we're stuck: which colliders are blocking?
            if (!this.placeable && Time.time - _lastBlockerDumpTime > 1f) {
                _lastBlockerDumpTime = Time.time;
                if (this.colliderTriggered.Count > 0) {
                    Debug.Log($"[BuildPreview/Stuck] blockers={this.colliderTriggered.Count} " +
                              $"names=[{string.Join(",", this.colliderTriggered.Select(c => c == null ? "null" : c.name + "(" + LayerMask.LayerToName(c.gameObject.layer) + ")"))}] " +
                              $"freeArea={this.haveFreeArea} ground={this.detectGround} rot={this.validRotation} inBuildable={this.isInBuildableArea}");
                }
            }
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

        private void OnTriggerEnter(Collider other) {
            if (other == this.collider || other.transform.IsChildOf(this.transform)) return;
            Debug.Log($"[BuildPreview/TriggerEnter] {other.name} layer={LayerMask.LayerToName(other.gameObject.layer)} tag={other.tag}");
            RouteOverlap(other);
            this.CheckValidity();
        }

        private void OnTriggerStay(Collider other) {
            // Ignore self / our own children — guards against a prop's child colliders
            // ever reaching this callback if a future prop hierarchy adds them.
            if (other == this.collider || other.transform.IsChildOf(this.transform)) return;

            RouteOverlap(other);
            this.CheckValidity();
        }

        private void OnTriggerExit(Collider other) {
            Debug.Log($"[BuildPreview/TriggerExit] {other.name} layer={LayerMask.LayerToName(other.gameObject.layer)} tag={other.tag}");
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

            if (this.restoreColliderIsTrigger && this.collider != null) {
                this.collider.isTrigger = this.colliderWasTrigger;
            }

            if (this.addedKinematicRigidbody != null) {
                Destroy(this.addedKinematicRigidbody);
                this.addedKinematicRigidbody = null;
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