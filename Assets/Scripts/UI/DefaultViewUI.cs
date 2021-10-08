using Sim.Interactables;
using Sim.UI;
using TMPro;
using UnityEngine;

namespace Sim {
    public class DefaultViewUI : MonoBehaviour {

        [Header("Settings")]
        [SerializeField]
        private CharacterInfoPanelUI characterInfoPanelUI;

        [SerializeField]
        private TextMeshProUGUI locationText;

        [SerializeField]
        private TextMeshProUGUI tenantText;
        
        [SerializeField]
        private RectTransform phone;

        [SerializeField]
        private PropsContentUI propsContentUI;

        [SerializeField]
        private ElevatorUI elevatorUI;

        public static DefaultViewUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
        }

        private void Start() {
            this.HidePropsContentUI();
            this.HideElevatorUI();
            this.SetLocationText("Salmon Hotel");
            this.SetTenantText(string.Empty);
        }

        public void SetLocationText(string value) {
            foreach (TextMeshProUGUI tmpPro in this.locationText.GetComponentsInChildren<TextMeshProUGUI>()) {
                tmpPro.text = value;
            }
        }
        
        public void SetTenantText(string value) {
            foreach (TextMeshProUGUI tmpPro in this.tenantText.GetComponentsInChildren<TextMeshProUGUI>()) {
                tmpPro.text = value;
            }
        }

        public void ShowElevatorUI(Teleporter elevator) {
            this.elevatorUI.Bind(elevator);
            this.elevatorUI.gameObject.SetActive(true);
        }

        public void HideElevatorUI() {
            this.elevatorUI.gameObject.SetActive(false);
        }

        public void ShowPropsContentUI(string[] items) {
            this.propsContentUI.Setup(items);
            this.propsContentUI.gameObject.SetActive(true);
        }
        
        public void RefreshPropsContentUI(string[] items) {
            this.propsContentUI.Setup(items);
        }

        public void HidePropsContentUI() {
            this.propsContentUI.gameObject.SetActive(false);
        }
    }
}

