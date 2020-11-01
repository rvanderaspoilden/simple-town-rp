using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class PersonnageResponse {
        [SerializeField] private Personnage[] personnages;

        public Personnage[] Personnages {
            get => personnages;
            set => personnages = value;
        }
    }
}