using System;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class ProfileResponse {
        [SerializeField] private User user;
        [SerializeField] private Personnage[] personnages;

        public User User {
            get => user;
            set => user = value;
        }

        public Personnage[] Personnages {
            get => personnages;
            set => personnages = value;
        }
    }
}