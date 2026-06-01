using System;
using Sim.Enums;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public struct Identity {
        public string firstname;

        public string lastname;

        public JobEnum job;

        public string Firstname {
            get => firstname;
            set => firstname = value;
        }

        public string Lastname {
            get => lastname;
            set => lastname = value;
        }

        public JobEnum Job {
            get => job;
            set => job = value;
        }

        public string FullName => $"{Firstname} {Lastname}";
    }
}