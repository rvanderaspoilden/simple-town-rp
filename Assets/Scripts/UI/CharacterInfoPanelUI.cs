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

        private void OnEnable() {
            Character.OnCharacterDataChanged += Setup;
        }

        private void OnDisable() {
            Character.OnCharacterDataChanged -= Setup;
        }

        public void Setup(CharacterData characterData) {
            this.SetText(this.nameText, characterData.Identity.FullName);
            this.SetText(this.jobText, "UNEMPLOYED");
            this.SetText(this.moneyText, characterData.Money.ToString());

            this.SetFillBarAmount(this.thirstBar, characterData.Health.Thirst / CommonConstants.MAX_BAR_AMOUNT);
            this.SetFillBarAmount(this.hungryBar, characterData.Health.Hungry / CommonConstants.MAX_BAR_AMOUNT);
            this.SetFillBarAmount(this.sleepBar, characterData.Health.Sleep / CommonConstants.MAX_BAR_AMOUNT);

            this.SetMood(DatabaseManager.GetMoodConfigByEnum(characterData.Mood));
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