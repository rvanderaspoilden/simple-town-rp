using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class DeliveryResponse {
        [SerializeField]
        private List<Delivery> deliveries;

        public List<Delivery> Deliveries {
            get => deliveries;
            set => deliveries = value;
        }
    }
}