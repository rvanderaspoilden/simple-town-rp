using System;
using DG.Tweening;
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
        private Image thirstBackgroundBar;

        [SerializeField]
        private Image hungryBackgroundBar;

        [SerializeField]
        private Image tirednessBackgroundBar;

        [SerializeField]
        private Color backgroundBarColorAlert;

        [SerializeField]
        private Image thirstBar;

        [SerializeField]
        private Image hungryBar;

        [SerializeField]
        private Image sleepBar;

        [SerializeField]
        private Image moodImage;

        private Sequence _thirstSequence;
        
        private Sequence _hungrySequence;
        
        private Sequence _tirednessSequence;

        private Color _initialBackgroundBarColor;

        public static CharacterInfoPanelUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
                this._initialBackgroundBarColor = this.thirstBackgroundBar.color;
                
                this._hungrySequence = DOTween.Sequence();
                this._thirstSequence = DOTween.Sequence();
                this._tirednessSequence = DOTween.Sequence();
                
                this.SetBackgroundBarSequence(this._hungrySequence, this.hungryBackgroundBar);
                this.SetBackgroundBarSequence(this._thirstSequence, this.thirstBackgroundBar);
                this.SetBackgroundBarSequence(this._tirednessSequence, this.tirednessBackgroundBar);
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
            this.SetMood(DatabaseManager.GetMoodConfigByEnum(characterData.Mood));
        }

        public void Setup(Home home) {
            this.addressHotelText.text = home.Address.street;
            this.addressDoorText.text = $"Étage {Mathf.CeilToInt(home.Address.doorNumber / (float) 6)}, Appt {home.Address.doorNumber}";
        }

        public void UpdateMoney(int money) {
            this.SetText(this.moneyText, money.ToString());
        }

        public void UpdateHealthUI(Health health) {
            this.SetFillBarAmount(this.thirstBar, health.Thirst / CommonConstants.MAX_BAR_AMOUNT);
            this.SetFillBarAmount(this.hungryBar, health.Hungry / CommonConstants.MAX_BAR_AMOUNT);
            this.SetFillBarAmount(this.sleepBar, health.Sleep / CommonConstants.MAX_BAR_AMOUNT);

            this.ManageHealthBarAnimation(health.Thirst, this._thirstSequence);
            this.ManageHealthBarAnimation(health.Hungry, this._hungrySequence);
            this.ManageHealthBarAnimation(health.Sleep, this._tirednessSequence);
        }

        private void ManageHealthBarAnimation(float value, Sequence sequence) {
            if (value <= 10) {
                sequence.Play();
            } else if (value > 10) {
                sequence.Pause();
            }
        }

        private void SetText(TextMeshProUGUI tmpPro, string value) {
            tmpPro.text = value;
        }

        private void SetMood(MoodConfig moodConfig) {
            moodImage.sprite = moodConfig.Sprite;
        }

        private void SetFillBarAmount(Image imageBar, float fillAmount) {
            imageBar.DOFillAmount(fillAmount, .5f);
        }

        private void SetBackgroundBarSequence(Sequence sequence, Image backgroundBar) {
            sequence.Append(backgroundBar.DOColor(this.backgroundBarColorAlert, .5f))
                .Append(backgroundBar.DOColor(this._initialBackgroundBarColor, .5f))
                .SetLoops(-1);

            sequence.OnPause(() => backgroundBar.color = this._initialBackgroundBarColor);
            sequence.Pause();
        }
    }
}