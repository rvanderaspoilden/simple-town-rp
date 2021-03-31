using Photon.Pun;
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
        private RectTransform phone;

        [SerializeField]
        private PropsContentUI propsContentUI;

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
            this.SetLocationText(PhotonNetwork.CurrentRoom.Name);
        }

        private void SetLocationText(string value) {
            foreach (TextMeshProUGUI tmpPro in this.locationText.GetComponentsInChildren<TextMeshProUGUI>()) {
                tmpPro.text = value;
            }
        }

        public void SetupPropsContentUI(string[] items) {
            this.propsContentUI.Setup(items);
            this.propsContentUI.gameObject.SetActive(true);
        }

        public void HidePropsContentUI() {
            this.propsContentUI.gameObject.SetActive(false);
        }
    }
}

