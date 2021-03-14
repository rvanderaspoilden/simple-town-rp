using System;
using Sim.Constants;
using Sim.Enums;

namespace Sim.Utils {
    public static class PlaceUtils {
        public static string ConvertPlaceEnumToSceneName(PlacesEnum place) {
            switch (place) {
                case PlacesEnum.TOWN_SQUARE:
                    return Scenes.TOWN_SQUARE;

                case PlacesEnum.HALL:
                    return Scenes.HALL;
                
                case PlacesEnum.HOME:
                    return Scenes.HOME;
            }

            throw new Exception("No scene name found for place enum : " + (string)Enum.Parse(typeof(PlacesEnum), place.ToString()));
        }
        
        public static string GetPlaceEnumName(PlacesEnum place) {
            switch (place) {
                case PlacesEnum.TOWN_SQUARE:
                    return PlaceName.TOWN_SQUARE;

                case PlacesEnum.HALL:
                    return PlaceName.HALL;
                
                case PlacesEnum.HOME:
                    return PlaceName.HOME;
            }

            throw new Exception("No place name found for place enum : " + (string)Enum.Parse(typeof(PlacesEnum), place.ToString()));
        }
    }
}