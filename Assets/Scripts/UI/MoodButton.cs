using System;
using Sim.Scriptables;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    public class MoodButton : MonoBehaviour {
        private MoodConfig moodConfig;

        private Image image;
        private Button button;

        public delegate void Click(MoodButton moodButton);

        public static event Click OnClick;

        private void Awake() {
            this.image = GetComponent<Image>();
            this.button = GetComponent<Button>();
        }

        private void OnEnable() {
            this.button.onClick.AddListener(() => OnClick?.Invoke(this));
        }

        private void OnDisable() {
            this.button.onClick.RemoveAllListeners();
        }

        public void Setup(MoodConfig config) {
            this.moodConfig = config;
            this.image.sprite = config.Sprite;
        }

        public MoodConfig MoodConfig => moodConfig;
    }

}
