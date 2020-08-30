﻿using System;
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
                    break;

                case BuildSurfaceEnum.GROUND:
                    return CatalogCategoryEnum.GROUND_PAINT;
                    break;
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
    }
}