using Sim.Entities;
using TMPro;
using UnityEngine;

namespace Sim {
    public class HomePanel : MonoBehaviour {
        [Header("Player card")]
        [SerializeField]
        private TextMeshProUGUI greetingText;

        [SerializeField]
        private TextMeshProUGUI levelText;

        [SerializeField]
        private TextMeshProUGUI moneyText;

        [Header("Version")]
        [SerializeField]
        private TextMeshProUGUI versionText;

        [Header("Style preview")]
        [SerializeField]
        private CharacterStyleSetup characterStyleSetup;

        private void Awake() {
            if (versionText != null) {
                versionText.text = $"DÉMO (version {Application.version})\nWORK IN PROGRESS";
            }
        }

        public void Bind(CharacterData character) {
            if (character == null) return;

            if (greetingText != null) {
                string firstname = character.Identity.Firstname;
                greetingText.text = string.IsNullOrEmpty(firstname)
                    ? "Salut, Citoyen !"
                    : $"Salut, {firstname} !";
            }

            if (levelText != null) {
                levelText.text = "Niveau 1";
            }

            if (moneyText != null) {
                moneyText.text = character.Money.ToString("N0");
            }

            if (characterStyleSetup != null) {
                characterStyleSetup.ApplyStyle(character.Style);
            }
        }

        public void Show() {
            this.gameObject.SetActive(true);
        }

        public void Hide() {
            this.gameObject.SetActive(false);
        }
    }
}
