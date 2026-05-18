using System;

namespace Sim.Entities {
    [Serializable]
    public struct CharacterJobStartRequest {
        public string characterId;
        public int category;
    }
}
