using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class CharacterResponse {
        [SerializeField]
        private CharacterData[] characters;

        public CharacterData[] Characters {
            get => characters;
            set => characters = value;
        }
    }
}