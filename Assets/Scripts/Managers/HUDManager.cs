using Photon.Pun;
using Sim.Building;
using Sim.UI;
using UnityEngine;

namespace Sim {
    public class HUDManager : MonoBehaviourPun {
        [Header("Settings")]
        [SerializeField]
        private AliDiscountCatalogUI aliDiscountCatalogUI;

        [SerializeField]
        private BuildPreviewPanelUI buildPreviewPanelUI;

        [SerializeField]
        private ContextMenuUI contextMenuUI;

        [SerializeField]
        private DefaultViewUI defaultViewUI;

        public static HUDManager Instance;

        private PanelTypeEnum currentPanelType;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
        }

        // Start is called before the first frame update
        void Start() {
            this.DisplayAdminPanel(false);
            this.DisplayPanel(PanelTypeEnum.DEFAULT);
            this.DisplayContextMenu(false, Vector3.zero);
        }

        public void DisplayPanel(PanelTypeEnum panelType) {
            if (panelType == PanelTypeEnum.BUILD) {
                this.buildPreviewPanelUI.gameObject.SetActive(true);
                this.defaultViewUI.gameObject.SetActive(false);
                this.DisplayAdminPanel(false);
            } else {
                this.defaultViewUI.gameObject.SetActive(true);
                this.buildPreviewPanelUI.gameObject.SetActive(false);
            }
        }

        public void DisplayAdminPanel(bool state) {
            this.aliDiscountCatalogUI.gameObject.SetActive(state);
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