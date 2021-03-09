using System;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class AuthenticationResponse {
        [SerializeField]
        private string access_token;

        public string GetAccessToken() {
            return this.access_token;
        }
    }
}