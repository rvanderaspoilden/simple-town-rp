using System;
using Sim.Entities;
using Sim.Enums;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class RoomNavigationData {
        [SerializeField]
        private RoomTypeEnum roomType;

        [SerializeField]
        private Address address;

        public RoomTypeEnum RoomType {
            get => roomType;
            set => roomType = value;
        }

        public Address Address {
            get => address;
            set => address = value;
        }

        public RoomNavigationData(RoomTypeEnum roomType, Address address) {
            this.roomType = roomType;
            this.address = address;
        }
    }
}