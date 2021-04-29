using System;
using Sim.Enums;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public struct Address {
        public HomeTypeEnum homeType;

        public int doorNumber;

        public string street;

        public override string ToString() {
            return $"{this.doorNumber}, {this.street}";
        }
    }
}