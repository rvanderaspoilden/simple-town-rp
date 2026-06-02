using TMPro;
using UnityEngine;

namespace Sim {
    public class AboutPanel : MonoBehaviour {
        [SerializeField]
        private TextMeshProUGUI versionText;

        private void Awake() {
            if (versionText != null) {
                versionText.text = $"Version {Application.version}";
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
