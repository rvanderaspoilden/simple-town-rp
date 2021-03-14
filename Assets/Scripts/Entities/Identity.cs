using System;
using Sim.Enums;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class Identity {
        [SerializeField]
        private string firstname;

        [SerializeField]
        private string lastname;

        [SerializeField]
        private string originCountry;

        [SerializeField]
        private JobEnum job;

        public string Firstname {
            get => firstname;
            set => firstname = value;
        }

        public string Lastname {
            get => lastname;
            set => lastname = value;
        }

        public string OriginCountry {
            get => originCountry;
            set => originCountry = value;
        }

        public JobEnum Job {
            get => job;
            set => job = value;
        }

        public string FullName => $"{Firstname} {Lastname}";
    }
}