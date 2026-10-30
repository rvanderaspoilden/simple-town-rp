using DG.Tweening;
using Sim.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Vital stats HUD (santé / faim / soif / énergie) based on the Synty
    /// HUD_Apocalypse_StatsList_03 prefab: one Slider per stat, filled 0..1.
    /// Replaces the old vitals bars of CharacterInfoPanelUI.
    /// </summary>
    public class PlayerStatsUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Slider healthSlider;

        [SerializeField]
        private Slider hungrySlider;

        [SerializeField]
        private Slider thirstSlider;

        [SerializeField]
        private Slider energySlider;

        [Tooltip("Below this value (0-100) the bar fill blinks to alert the player")]
        [SerializeField]
        private float alertThreshold = 10f;

        [SerializeField]
        private Color alertColor = new Color(1f, 0.25f, 0.2f);

        private Sequence _healthSequence;
        private Sequence _hungrySequence;
        private Sequence _thirstSequence;
        private Sequence _energySequence;

        public static PlayerStatsUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;

                this._healthSequence = this.CreateAlertSequence(this.healthSlider);
                this._hungrySequence = this.CreateAlertSequence(this.hungrySlider);
                this._thirstSequence = this.CreateAlertSequence(this.thirstSlider);
                this._energySequence = this.CreateAlertSequence(this.energySlider);
            }
        }

        public void UpdateStats(Health health) {
            this.SetBar(this.healthSlider, this._healthSequence, health.Pv);
            this.SetBar(this.hungrySlider, this._hungrySequence, health.Hungry);
            this.SetBar(this.thirstSlider, this._thirstSequence, health.Thirst);
            // L'énergie n'est plus pilotée par la santé : c'est désormais une barre
            // d'endurance locale mise à jour par PlayerController (voir SetEnergy).
        }

        /// <summary>Met à jour la barre d'endurance (0-100). Pilotée chaque frame par
        /// PlayerController (owner local), donc sans tween — la valeur arrive déjà lissée —
        /// pour éviter d'empiler des tweens DOTween à chaque frame.</summary>
        public void SetEnergy(float value) {
            if (this.energySlider == null) return;

            this.energySlider.value = value / CommonConstants.MAX_BAR_AMOUNT;

            if (value <= this.alertThreshold) {
                this._energySequence.Play();
            } else {
                this._energySequence.Pause();
            }
        }

        private void SetBar(Slider slider, Sequence alertSequence, float value) {
            slider.DOValue(value / CommonConstants.MAX_BAR_AMOUNT, .5f);

            if (value <= this.alertThreshold) {
                alertSequence.Play();
            } else {
                alertSequence.Pause();
            }
        }

        private Sequence CreateAlertSequence(Slider slider) {
            Image fill = slider.fillRect.GetComponent<Image>();
            Color initialColor = fill.color;

            Sequence sequence = DOTween.Sequence()
                .Append(fill.DOColor(this.alertColor, .5f))
                .Append(fill.DOColor(initialColor, .5f))
                .SetLoops(-1);

            sequence.OnPause(() => fill.color = initialColor);
            sequence.Pause();

            return sequence;
        }
    }
}
