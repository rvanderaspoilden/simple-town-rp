using System;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class User {
        [SerializeField] private string _id;
        [SerializeField] private string username;
        [SerializeField] private string email;

        public string Id {
            get => _id;
            set => _id = value;
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