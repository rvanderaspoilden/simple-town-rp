using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class PersonnageResponse {
        [SerializeField] private CharacterData[] personnages;

        public CharacterData[] Personnages {
            get => personnages;
            set => personnages = value;
        }
    }
}