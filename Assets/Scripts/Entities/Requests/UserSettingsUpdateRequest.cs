using System;
using UnityEngine;

namespace Sim.Entities {
    /// <summary>
    /// Body for PUT /user-settings/by-user/:userId. Wraps the JsonUtility-
    /// serialized `data` payload (matches the backend DTO shape).
    /// </summary>
    [Serializable]
    public class UserSettingsUpdateRequest {
        [SerializeField] private UserSettingsData data;

        public UserSettingsUpdateRequest() {}
        public UserSettingsUpdateRequest(UserSettingsData data) { this.data = data; }

        public UserSettingsData Data {
            get => data;
            set => data = value;
        }
    }
}
