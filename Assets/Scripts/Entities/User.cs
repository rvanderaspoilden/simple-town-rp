using System;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class User {
        [SerializeField] private string id;
        [SerializeField] private string username;
        [SerializeField] private string email;

        public string Id {
            get => id;
            set => id = value;
        }

        public string Username {
            get => username;
            set => username = value;
        }

        public string Email {
            get => email;
            set => email = value;
        }
    }
}