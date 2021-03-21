using System;
using Photon.Pun;
using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sim.Utils {
    public static class CommonUtils {
        public static CatalogCategoryEnum ConvertBuildSurfaceToCategory(BuildSurfaceEnum surfaceEnum) {
            switch (surfaceEnum) {
                case BuildSurfaceEnum.WALL:
                    return CatalogCategoryEnum.WALL_COVERING;

                case BuildSurfaceEnum.GROUND:
                    return CatalogCategoryEnum.GROUND_COVERING;
            }

            throw new Exception("No converter found");
        }

        public static int GetApartmentFloor(int doorNumber, int limit) {
            return Mathf.CeilToInt(doorNumber / (float) limit);
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
        public static int GetLayerMaskSurfacesToPaint(PaintConfig paintConfig) {
            if (paintConfig.IsGroundCover()) {
                return (1 << 9); // Ground Layer
            } else if (paintConfig.IsWallCover()) {
                return (1 << 12); // Wall layer
            }

            throw new Exception($"No surface type is defined for paint config ID => {paintConfig.GetId()}");
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
            }

            throw new Exception($"No scene name associated to roomTypeEnum => {roomType}");
        }
    }
}