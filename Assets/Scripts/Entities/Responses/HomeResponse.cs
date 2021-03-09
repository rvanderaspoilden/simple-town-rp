using System;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class HomeResponse {
        [SerializeField]
        private Home[] homes;

        public Home[] Homes {
            get => homes;
            set => homes = value;
        }
    }
}