using Sim.Interactables;
using Sim.UI;
using UnityEngine;

namespace Sim {
    public class HUDManager : MonoBehaviour {
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
            this.DisplayPanel(PanelTypeEnum.NONE);
            this.CloseContextMenu();
        }

        public void DisplayPanel(PanelTypeEnum panelType) {
            if (panelType == PanelTypeEnum.BUILD) {
                this.buildPreviewPanelUI.gameObject.SetActive(true);
                this.defaultViewUI.gameObject.SetActive(false);
                this.DisplayAdminPanel(false);
            } else if (panelType == PanelTypeEnum.DEFAULT) {
                this.defaultViewUI.gameObject.SetActive(true);
                this.buildPreviewPanelUI.gameObject.SetActive(false);
            } else {
                this.defaultViewUI.gameObject.SetActive(false);
                this.buildPreviewPanelUI.gameObject.SetActive(false);
            }
        }

        public void DisplayAdminPanel(bool state) {
            this.aliDiscountCatalogUI.gameObject.SetActive(state);
        }

        public void ShowContextMenu(Action[] actions = null, Transform target = null, bool withPriority = false) {
            this.radialMenuUI.Setup(target, actions, withPriority);
        }

        public void CloseContextMenu() {
            this.radialMenuUI.Close();
        }
    }
}