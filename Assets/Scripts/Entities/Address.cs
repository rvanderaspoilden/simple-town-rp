using System;
using Sim.Enums;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class Address {
        [SerializeField]
        private HomeTypeEnum homeType;

        [SerializeField]
        private int doorNumber;

        [SerializeField]
        private string street;

        public HomeTypeEnum HomeType {
            get => homeType;
            set => homeType = value;
        }

        public int DoorNumber {
            get => doorNumber;
            set => doorNumber = value;
        }

        public string Street {
            get => street;
            set => street = value;
        }

        public override string ToString() {
            return $"{this.doorNumber}, {this.Street}";
        }
    }
}