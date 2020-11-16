using Sim.Entities;
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

        public void Setup(Personnage personnage) {
            this.SetText(this.nameText, personnage.GetFullName());
            this.SetText(this.jobText, personnage.Job);
            this.SetText(this.moneyText, personnage.Money.ToString());
            
            this.SetFillBarAmount(this.thirstBar, personnage.VitalInformation.GetThirst() / CommonConstants.MAX_BAR_AMOUNT);
            this.SetFillBarAmount(this.hungryBar, personnage.VitalInformation.GetHungry() / CommonConstants.MAX_BAR_AMOUNT);
            this.SetFillBarAmount(this.sleepBar, personnage.VitalInformation.GetSleep() / CommonConstants.MAX_BAR_AMOUNT);
        }

        private void SetText(TextMeshProUGUI tmpPro, string value) {
            tmpPro.text = value;
        }

        private void SetFillBarAmount(Image imageBar, float fillAmount) {
            imageBar.fillAmount = fillAmount;
        }
    }
}
