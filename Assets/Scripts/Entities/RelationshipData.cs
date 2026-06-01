using System;
using System.Collections.Generic;

namespace Sim.Entities {
    /// <summary>Relationship state between the local character and another. Order
    /// matters: identity is revealed at Acquaintance and above (>= comparison).</summary>
    public enum RelationshipState {
        Unknown = 0,
        Acquaintance = 1,
        Contact = 2,
    }

    /// <summary>Mirrors a backend relationship row, mapped to the "other" side.
    /// Deserialized with JsonUtility — field names must match the backend JSON.</summary>
    [Serializable]
    public class RelationshipData {
        public string otherCharacterId;
        public string state;   // "acquaintance" | "contact"
        public string metAt;

        public RelationshipState State {
            get {
                switch (this.state) {
                    case "acquaintance": return RelationshipState.Acquaintance;
                    case "contact":      return RelationshipState.Contact;
                    default:             return RelationshipState.Unknown;
                }
            }
        }
    }

    [Serializable]
    public class RelationshipResponse {
        public List<RelationshipData> relationships = new List<RelationshipData>();
    }

    /// <summary>Body for POST /relationships (sent by the Unity server on accept).</summary>
    [Serializable]
    public class CreateRelationshipBody {
        public string characterA;
        public string characterB;
    }
}
