using System;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class Health {
        [SerializeField]
        private float thirst;

        [SerializeField]
        private float hungry;

        [SerializeField]
        private float sleep;

        [SerializeField]
        private float pv = 100;

        public float Thirst {
            get => thirst;
            set => thirst = value;
        }

        public float Hungry {
            get => hungry;
            set => hungry = value;
        }

        public float Sleep {
            get => sleep;
            set => sleep = value;
        }

        public float Pv {
            get => pv;
            set => pv = value;
        }
    }
}