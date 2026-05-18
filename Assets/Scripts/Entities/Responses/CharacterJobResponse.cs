using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class CharacterJobResponse {
        [SerializeField]
        private CharacterJobData[] characterJobs;

        public CharacterJobData[] CharacterJobs {
            get => characterJobs;
            set => characterJobs = value;
        }
    }
}
