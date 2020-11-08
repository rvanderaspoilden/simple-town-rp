using System;
using Sim.Enums;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sim.Utils {
    public static class CommonUtils {
        public static CatalogCategoryEnum ConvertBuildSurfaceToCategory(BuildSurfaceEnum surfaceEnum) {
            switch (surfaceEnum) {
                case BuildSurfaceEnum.WALL:
                    return CatalogCategoryEnum.WALL_PAINT;

                case BuildSurfaceEnum.GROUND:
                    return CatalogCategoryEnum.GROUND_PAINT;
            }

            throw new Exception("Not converter found");
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

        public static int GetDoorNumberFromFloorNumber(int initialNumber) {
            return initialNumber + (CommonConstants.appartmentLimitPerFloor * (CommonUtils.GetAppartmentFloorFromAppartmentId(NetworkManager.Instance.Personnage.AppartmentId, CommonConstants.appartmentLimitPerFloor) - 1));
        }
    }
}