using System;
using UnityEngine;

namespace Sim.Entities {
    /// <summary>
    /// Per-user preferences (notifications, audio, graphics, …). Persisted in
    /// the backend `user_settings` table as a JSONB blob. The whole class is
    /// re-serialized via JsonUtility on every save — there is no per-field
    /// patch endpoint by design.
    /// </summary>
    [Serializable]
    public class UserSettings {
        [SerializeField] private string user_id;
        [SerializeField] private UserSettingsData data = new UserSettingsData();

        public string UserId {
            get => user_id;
            set => user_id = value;
        }

        public UserSettingsData Data {
            get => data ??= new UserSettingsData();
            set => data = value ?? new UserSettingsData();
        }
    }

    [Serializable]
    public class UserSettingsData {
        [SerializeField] private bool notificationsNewMission = true;
        [SerializeField, Range(0f, 1f)] private float audioMaster = 1f;
        [SerializeField, Range(0f, 1f)] private float audioMusic = 1f;
        [SerializeField, Range(0f, 1f)] private float audioSfx = 1f;
        [Tooltip("Unity quality level index (matches QualitySettings.SetQualityLevel).")]
        [SerializeField] private int graphicsQuality = 2;

        public bool NotificationsNewMission {
            get => notificationsNewMission;
            set => notificationsNewMission = value;
        }

        public float AudioMaster {
            get => audioMaster;
            set => audioMaster = Mathf.Clamp01(value);
        }

        public float AudioMusic {
            get => audioMusic;
            set => audioMusic = Mathf.Clamp01(value);
        }

        public float AudioSfx {
            get => audioSfx;
            set => audioSfx = Mathf.Clamp01(value);
        }

        public int GraphicsQuality {
            get => graphicsQuality;
            set => graphicsQuality = value;
        }
    }
}
