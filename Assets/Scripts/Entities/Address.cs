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
        private BuildingEnum building;

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

        public BuildingEnum Building {
            get => building;
            set => building = value;
        }

        public string Street {
            get => street;
            set => street = value;
        }
    }
}