using System;

namespace Sim.Entities {
    [Serializable]
    public struct CharacterCreationRequest {
        public string firstname;

        public string lastname;

        public string originCountry;

        public string entranceDate;

        public Style style;
    }
}