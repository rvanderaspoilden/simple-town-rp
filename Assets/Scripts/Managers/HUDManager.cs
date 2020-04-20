using Photon.Pun;
using Sim.Enums;
using Sim.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class HUDManager : MonoBehaviourPun {
        [Header("Settings")]
        [SerializeField] private Button modeButton;
        [SerializeField] private CatalogUI catalogUI;

        public static HUDManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
        }

        // Start is called before the first frame update
        void Start()
        {
            this.RefreshModeButton();
        }

        public void ToggleMode() {
            CameraManager.Instance.ToggleMode();
            this.RefreshModeButton();
        }

        private void RefreshModeButton() {
            this.modeButton.interactable = PhotonNetwork.IsMasterClient;
            this.modeButton.GetComponentInChildren<TextMeshProUGUI>().text = CameraManager.Instance.GetCurrentMode() == CameraModeEnum.FREE ? "mode construction" : "terminer";
            this.catalogUI.gameObject.SetActive(CameraManager.Instance.GetCurrentMode() == CameraModeEnum.BUILD);
        }
    }   
}
