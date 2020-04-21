using System;
using Sim.Enums;

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
    }
}