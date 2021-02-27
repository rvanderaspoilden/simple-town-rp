using System;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class ProfileResponse {
        [SerializeField] private User user;
        [SerializeField] private CharacterData[] personnages;

        public User User {
            get => user;
            set => user = value;
        }

        public CharacterData[] Personnages {
            get => personnages;
            set => personnages = value;
        }
    }
}