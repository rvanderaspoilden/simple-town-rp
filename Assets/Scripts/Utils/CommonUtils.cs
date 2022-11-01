using System;
using Interaction;
using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

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
         * Throw exception if different of wall or ground props
         */
        public static int GetLayerMaskSurfacesToPose(Props props) {
            int layerValue = (1 << 12);
            if (props.IsGroundProps()) {
                layerValue = layerValue | (props.GetConfiguration().IsPosableOnProps() ? (1 << 9 | 1 << 16) : (1 << 9)); // Ground + SuperPosable layers
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
        
        public static bool CanInteractWith(this PlayerController player, IInteractable interactable, Vector3 originPoint) {
            float maxRange = interactable.GetRange();
            Vector3 origin = Vector3.Scale(originPoint, new Vector3(1, 0, 1));
            Vector3 target = Vector3.Scale(player.transform.position, new Vector3(1, 0, 1));

            if (interactable.GetActions()?.Length <= 0 || Mathf.Abs(Vector3.Distance(origin, target)) > maxRange) {
                return false;
            }

            Vector3 dir = originPoint - player.GetHeadTargetForCamera().position;
            RaycastHit hit;
            
            if (Physics.Raycast(player.GetHeadTargetForCamera().position, dir, out hit)) {
                return interactable.Equals(hit.collider.GetComponentInParent<IInteractable>());
            }

            return false;
        }
    }
}