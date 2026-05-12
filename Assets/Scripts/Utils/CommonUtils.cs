using System;
using Interaction;
using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sim.Utils {
    public static class CommonUtils {
        public static int GetApartmentFloor(int doorNumber, int limit) {
            return Mathf.CeilToInt(doorNumber / (float) limit);
        }
        
        public static float[] ColorToArray(Color color) {
            return new float[4] {color.r, color.g, color.b, color.a};
        } 
        
        public static Color ArrayToColor(float[] color) {
            if (color.Length < 3) {
                throw new Exception("[SAVE UTILS] Failed to convert color array");
            }
            
            return new Color(color[0], color[1], color[2], color.Length > 3 ? color[3] : 1);
        } 

        /**
         * This method is used to give layers on which ones the props can be posed
         */
        public static int GetLayerMaskSurfacesToPose(PropBehaviourBase behaviour) {
            if (behaviour.IsRoofProps()) {
                return (1 << 17); // Roof layer only — roof props can only stick under the roof
            }

            int layerValue = (1 << 12);
            if (behaviour.IsGroundProps()) {
                layerValue = layerValue | (behaviour.GetConfiguration().IsPosableOnProps() ? (1 << 9 | 1 << 16) : (1 << 9)); // Ground + SuperPosable layers
            }

            return layerValue;
        }

        /**
         * This method is used to give layers on which ones the paint can be used
         * Throw exception if different of wall or ground
         */
        public static int GetLayerMaskSurfacesToPaint(CoverConfig coverConfig) {
            if (coverConfig.IsGroundCover()) {
                return (1 << 9); // Ground Layer
            } else if (coverConfig.IsWallCover()) {
                return (1 << 12); // Wall layer
            }

            throw new Exception($"No surface type is defined for paint config ID => {coverConfig.GetId()}");
        }

        public static int GetDoorNumberFromFloorNumber(int initialNumber, int relativeDoorNumber) {
            return initialNumber + (CommonConstants.appartmentLimitPerFloor *
                                    (GetApartmentFloor(relativeDoorNumber,
                                        CommonConstants.appartmentLimitPerFloor) - 1));
        }

        public static string GetDate() {
            DateTime date = DateTime.Now;

            return date.ToString("dd/MM/yyyy");
        }

        public static string GetSceneName(RoomTypeEnum roomType) {
            if (roomType.Equals(RoomTypeEnum.HOME)) {
                return "Home";
            } else if (roomType.Equals(RoomTypeEnum.BUILDING_HALL)) {
                return "Hall";
            } else if (roomType.Equals(RoomTypeEnum.ENTRANCE)) {
                return "Entrance";
            }

            throw new Exception($"No scene name associated to roomTypeEnum => {roomType}");
        }
        
        /// <summary>
        /// Returns true when the IInteractable's underlying Unity object exists and has not been destroyed.
        /// Must cast to UnityEngine.Object explicitly — interface references bypass Unity's == operator override,
        /// so a plain != null check returns true even for destroyed MonoBehaviours.
        /// </summary>
        public static bool IsAlive(this IInteractable interactable) {
            return interactable != null && (interactable as UnityEngine.Object) != null;
        }

        public static bool CanInteractWith(this PlayerController player, IInteractable interactable, Vector3 originPoint) {
            float maxRange = interactable.GetRange();
            Vector3 origin = Vector3.Scale(originPoint, new Vector3(1, 0, 1));
            Vector3 target = Vector3.Scale(player.transform.position, new Vector3(1, 0, 1));

            if (!interactable.IsInteractable() || interactable.GetActions()?.Length <= 0 || Mathf.Abs(Vector3.Distance(origin, target)) > maxRange) {
                return false;
            }

            // Line-of-sight: ensure no solid geometry sits between the player's head
            // and the prop's hit surface. RaycastAll (trigger-ignoring) — the
            // interactable is reachable iff it appears in the ordered hit list
            // before any non-interactable collider does. This correctly handles
            // roof/wall-mounted props (a single Physics.Raycast can otherwise
            // pick up scenery colliders near the prop and return them instead
            // of the prop itself).
            Vector3 head = player.GetHeadTargetForCamera().position;
            Vector3 dir  = originPoint - head;
            float   dist = dir.magnitude;
            if (dist <= 0.001f) return true;

            RaycastHit[] hits = Physics.RaycastAll(head, dir.normalized, dist + 0.1f, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return true;     // nothing in the way → fine

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits) {
                if (h.collider.transform.IsChildOf(player.transform)) continue; // ignore the player's own colliders
                IInteractable hitInteractable = h.collider.GetComponentInParent<IInteractable>();
                if (interactable.Equals(hitInteractable)) return true;          // reached the prop
                if (hitInteractable == null) return false;                       // solid scenery blocks LoS
                // Otherwise it's a different interactable in the way — keep looking
            }
            return false;
        }

        public static void ClearChildren(Transform transform) {
            foreach (Transform child in transform) {
                Object.Destroy(child.gameObject);
            }
        }
    }
}