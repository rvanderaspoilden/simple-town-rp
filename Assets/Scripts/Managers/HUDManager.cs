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
        private RadialMenuUI radialMenuUI;

        [SerializeField]
        private DefaultViewUI defaultViewUI;

        public static HUDManager Instance;

        private PanelTypeEnum currentPanelType;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
            
            DontDestroyOnLoad(this.gameObject);
        }

        // Start is called before the first frame update
        void Start() {
            this.DisplayAdminPanel(false);
            this.DisplayPanel(PanelTypeEnum.DEFAULT);
            this.DisplayContextMenu(false);
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

        public void DisplayContextMenu(bool state, Props interactedProp = null, bool withPriority = false) {
            if ((!state && this.radialMenuUI.gameObject.activeSelf) || (state && !this.radialMenuUI.gameObject.activeSelf)) {
                this.radialMenuUI.gameObject.SetActive(state);
            }

            if (interactedProp) {
                this.radialMenuUI.Setup(interactedProp, withPriority);
            }
        }
    }
}