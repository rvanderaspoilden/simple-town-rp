using Photon.Pun;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using Sim.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class HUDManager : MonoBehaviourPun {
        [Header("Settings")]
        [SerializeField] private AdminPanelUI adminPanelUI;
        [SerializeField] private BuildPreviewPanelUI buildPreviewPanelUI;
        [SerializeField] private ContextMenuUI contextMenuUI;

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
            this.DisplayContextMenu(false, Vector3.zero);
        }

        public void DisplayAdminPanel(bool state) {
            this.adminPanelUI.gameObject.SetActive(state);
        }

        public void DisplayBuildPreviewPanel(bool state) {
            this.buildPreviewPanelUI.gameObject.SetActive(state);
        }

        public void DisplayContextMenu(bool state, Vector3 position, Props interactedProp = null) {
            this.contextMenuUI.transform.position = position;

            if (interactedProp) {
                this.contextMenuUI.Setup(interactedProp);
            }
            this.contextMenuUI.gameObject.SetActive(state);
        }
    }   
}
