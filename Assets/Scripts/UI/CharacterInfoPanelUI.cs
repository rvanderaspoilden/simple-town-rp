using System;
using Sim.Entities;
using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    public class CharacterInfoPanelUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private TextMeshProUGUI nameText;

        [SerializeField]
        private TextMeshProUGUI addressHotelText;

        [SerializeField]
        private TextMeshProUGUI addressDoorText;

        [SerializeField]
        private TextMeshProUGUI jobText;

        [SerializeField]
        private TextMeshProUGUI moneyText;

        [SerializeField]
        private Image thirstBar;

        [SerializeField]
        private Image hungryBar;

        [SerializeField]
        private Image sleepBar;

        [SerializeField]
        private Image moodImage;

        public static CharacterInfoPanelUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
        }

        private void OnEnable() {
            PlayerController.OnCharacterDataChanged += Setup;
        }

        private void OnDisable() {
            PlayerController.OnCharacterDataChanged -= Setup;
        }

        public void Setup(CharacterData characterData) {
            this.SetText(this.nameText, characterData.Identity.FullName);
            this.SetText(this.jobText, "CHÔMEUR");
            this.SetText(this.moneyText, characterData.Money.ToString());

            this.SetFillBarAmount(this.thirstBar, characterData.Health.Thirst / CommonConstants.MAX_BAR_AMOUNT);
            this.SetFillBarAmount(this.hungryBar, characterData.Health.Hungry / CommonConstants.MAX_BAR_AMOUNT);
            this.SetFillBarAmount(this.sleepBar, characterData.Health.Sleep / CommonConstants.MAX_BAR_AMOUNT);

            this.SetMood(DatabaseManager.GetMoodConfigByEnum(characterData.Mood));
        }

        public void Setup(Home home) {
            this.addressHotelText.text = home.Address.street;
            this.addressDoorText.text = $"Étage {Mathf.CeilToInt(home.Address.doorNumber / (float)6)}, Appt {home.Address.doorNumber}";
        }

        private void SetText(TextMeshProUGUI tmpPro, string value) {
            tmpPro.text = value;
        }

        private void SetMood(MoodConfig moodConfig) {
            moodImage.sprite = moodConfig.Sprite;
        }

        private void SetFillBarAmount(Image imageBar, float fillAmount) {
            imageBar.fillAmount = fillAmount;
        }
    }
}