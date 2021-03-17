using System;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class HttpException {
        [SerializeField]
        private int statusCode;

        [SerializeField]
        private string message;

        public int StatusCode {
            get => statusCode;
            set => statusCode = value;
        }

        public string Message {
            get => message;
            set => message = value;
        }
    }
}