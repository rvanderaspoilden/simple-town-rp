using UnityEngine;

namespace Sim.Entities {
    public struct AssignApartmentRequest {
        [SerializeField]
        private string characterId;

        [SerializeField]
        private string presetName;

        public AssignApartmentRequest(string characterId, string presetName) {
            this.characterId = characterId;
            this.presetName = presetName;
        }
    }
}