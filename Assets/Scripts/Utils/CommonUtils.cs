using System;
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

        public static string GetRelativePathFromResources(Object gameObject) {
            string[] parts = AssetDatabase.GetAssetPath(gameObject).Split("/".ToCharArray());
            string path = "";

            for (int i = 2; i < parts.Length; i++) {
                if (i == parts.Length - 1) {
                    path += parts[i].Substring(0, parts[i].Length - 7);
                } else {
                    path += parts[i] + "/";
                }
            }

            return path;
        }

        public static int GetAppartmentFloorFromAppartmentId(int appartmentId, int limit) {
            return Mathf.CeilToInt(appartmentId / (float) limit);
        }

        /**
         * This method is used to give layers on which ones the props can be posed
         * Throw exception if different of wall or ground props
         */
        public static int GetLayerMaskSurfacesToPose(Props props) {
            if (props.IsGroundProps()) {
                return props.GetConfiguration().IsPosableOnProps() ? (1 << 9 | 1 << 16) : (1 << 9); // Ground + SuperPosable layers
            } else if (props.IsWallProps()) {
                return (1 << 12); // Wall layer
            }
            
            throw new Exception($"No surface type is defined for props ID => {props.GetConfiguration().GetId()}");
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

        public static int GetDoorNumberFromFloorNumber(int initialNumber) {
            return initialNumber + (CommonConstants.appartmentLimitPerFloor *
                                    (CommonUtils.GetAppartmentFloorFromAppartmentId(NetworkManager.Instance.Personnage.AppartmentId,
                                        CommonConstants.appartmentLimitPerFloor) - 1));
        }
    }
}