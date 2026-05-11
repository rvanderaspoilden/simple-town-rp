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

            foreach (Collider other in hits) {
                if (other == this.collider || other.transform.IsChildOf(this.transform)) continue;

                // Only seed colliders that actually penetrate the prop's OBB. OverlapBox
                // can report contacts that Unity's trigger system never registers — if we
                // seed those, no OnTriggerExit will ever clean them up. FixedUpdate prunes
                // anything that becomes stale anyway, but we minimize false positives here.
                bool penetrating = Physics.ComputePenetration(
                    this.collider, this.transform.position, this.transform.rotation,
                    other,          other.transform.position, other.transform.rotation,
                    out _, out _);
                if (!penetrating) continue;

                RouteOverlap(other);
            }
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

        private void FixedUpdate() {
            // Prune stale blockers: Unity's trigger system and Physics.ComputePenetration
            // don't always agree (especially with concave MeshColliders), so an OnTriggerExit
            // may never fire for a contact we added on seed. Re-check each tracked blocker
            // and drop it if there's no real penetration anymore.
            if (this.collider == null || this.colliderTriggered.Count == 0) return;

            for (int i = this.colliderTriggered.Count - 1; i >= 0; i--) {
                Collider other = this.colliderTriggered[i];
                if (other == null) { this.colliderTriggered.RemoveAt(i); continue; }
                if (!other.gameObject.activeInHierarchy) continue;

                bool stillOverlapping = Physics.ComputePenetration(
                    this.collider, this.transform.position, this.transform.rotation,
                    other,          other.transform.position, other.transform.rotation,
                    out _, out _);
                if (!stillOverlapping) this.colliderTriggered.RemoveAt(i);
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
            this.placeable = this.haveFreeArea && this.detectGround && this.validRotation && this.isInBuildableArea;

            this.propsRenderer.SetPreviewState(this.placeable ? PreviewStateEnum.VALID : PreviewStateEnum.ERROR);

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