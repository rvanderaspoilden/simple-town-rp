using Photon.Pun;
using Sim.Enums;
using Sim.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class HUDManager : MonoBehaviourPun {
        [Header("Settings")]
        [SerializeField] private AdminPanelUI adminPanelUI;
        [SerializeField] private BuildPreviewPanelUI buildPreviewPanelUI;

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
            this.DisplayAdminPanel(false);
            this.DisplayBuildPreviewPanel(false);
        }

        public void DisplayAdminPanel(bool state) {
            this.adminPanelUI.gameObject.SetActive(state);
        }

        public void DisplayBuildPreviewPanel(bool state) {
            this.buildPreviewPanelUI.gameObject.SetActive(state);
        }
    }   
}
