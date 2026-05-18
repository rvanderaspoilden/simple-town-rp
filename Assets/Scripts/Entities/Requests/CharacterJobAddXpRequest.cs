using System;

namespace Sim.Entities {
    [Serializable]
    public struct CharacterJobAddXpRequest {
        public string characterId;
        public int category;
        public int delta;
    }
}
