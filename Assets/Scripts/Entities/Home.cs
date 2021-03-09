using System;
using Sim.Enums;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class Home {
        [SerializeField]
        private string _id;
        
        [SerializeField]
        private Address address;


        [SerializeField]
        private string owner;


        [SerializeField]
        private HomeTypeEnum type;


        [SerializeField]
        private string tenant;


        [SerializeField]
        private int purchasePrice;


        [SerializeField]
        private int rentPrice;


        [SerializeField]
        private string preset;


        [SerializeField]
        private SceneData sceneData;

        public string Id {
            get => _id;
            set => _id = value;
        }

        public Address Address {
            get => address;
            set => address = value;
        }

        public string Owner {
            get => owner;
            set => owner = value;
        }

        public HomeTypeEnum Type {
            get => type;
            set => type = value;
        }

        public string Tenant {
            get => tenant;
            set => tenant = value;
        }

        public int PurchasePrice {
            get => purchasePrice;
            set => purchasePrice = value;
        }

        public int RentPrice {
            get => rentPrice;
            set => rentPrice = value;
        }

        public string Preset {
            get => preset;
            set => preset = value;
        }

        public SceneData SceneData {
            get => sceneData;
            set => sceneData = value;
        }
    }
}