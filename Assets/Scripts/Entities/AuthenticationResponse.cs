using System;
using UnityEngine;

namespace Sim {
    [System.Serializable]
    public class AuthenticationResponse {
        [SerializeField] private String access_token;

        public String GetAccessToken() {
            return this.access_token;
        }
    }
}