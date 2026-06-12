using System;
using System.Linq;
using Mirror;
using Network.Messages;
using Sim.Building;
using Sim.Interactables;
using Sim.UI;
using TMPro;
using UI.Build_Panel;
using UnityEngine;
using Action = System.Action;

namespace Sim {
    public class DefaultViewUI : MonoBehaviour {

        [Header("Settings")]
        [SerializeField]
        private CharacterInfoPanelUI characterInfoPanelUI;

        [Tooltip("Root of the top-right location panel. Hidden when there is no location text to display.")]
        [SerializeField]
        private GameObject locationPanel;

        [SerializeField]
        private TextMeshProUGUI locationText;

        [Tooltip("Optional subtitle TMP shown right under the main location text. Receives everything after the first ', ' of the location string (e.g. 'ÉTAGE 1, PORTE 6' for 'SALMON HOTEL, ÉTAGE 1, PORTE 6'). Hidden when there's no subtitle to show.")]
        [SerializeField]
        private TextMeshProUGUI locationSubtitleText;

        [SerializeField]
        private TextMeshProUGUI tenantText;
        
        [SerializeField]
        private RectTransform phone;

        [SerializeField]
        private PropsContentUI propsContentUI;

        [SerializeField]
        private ElevatorUI elevatorUI;

        [SerializeField]
        private AdminPanelManager adminPanelManager;

        [SerializeField]
        private SubGamePanelUI subGamePanelUI;

        [SerializeField]
        private BuildPanelUI buildPanelUI;
        
        public static DefaultViewUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
        }

        private void Start() {
            this.adminPanelManager.gameObject.SetActive(false);
            this.DisplayBuildPanel(false);
            this.HidePropsContentUI();
            this.HideElevatorUI();
            this.SetLocationText("Salmon Hotel");
            this.SetTenantText(string.Empty);
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F2)) {
                this.ToggleAdminPanel();
            }
        }

        public void SetLocationText(string value) {
            bool has = !string.IsNullOrWhiteSpace(value);
            string main = string.Empty;
            string sub  = string.Empty;
            if (has) {
                // Split on first ", " — "SALMON HOTEL, ÉTAGE 1, PORTE 6" → main
                // "SALMON HOTEL" + subtitle "ÉTAGE 1, PORTE 6". No comma → all in main.
                int sep = value.IndexOf(", ", System.StringComparison.Ordinal);
                if (sep < 0) {
                    main = value;
                } else {
                    main = value.Substring(0, sep);
                    sub  = value.Substring(sep + 2);
                }
            }
            if (this.locationText != null) this.locationText.text = main;
            if (this.locationSubtitleText != null) {
                this.locationSubtitleText.text = sub;
                this.locationSubtitleText.gameObject.SetActive(!string.IsNullOrWhiteSpace(sub));
            }
            // Hide the whole panel (location + subtitle + tenant + background) when
            // there's nothing to display — the player is in unknown territory.
            if (this.locationPanel != null) this.locationPanel.SetActive(has);
        }

        public void SetTenantText(string value) {
            bool has = !string.IsNullOrWhiteSpace(value);
            if (this.tenantText != null) {
                this.tenantText.text = has ? value : string.Empty;
                // Toggle the tenant child active state so the VerticalLayoutGroup
                // recomputes the panel height (childControlHeight=false, but the
                // layout ignores inactive children).
                this.tenantText.gameObject.SetActive(has);
            }
        }

        public void ShowElevatorUI(TeleporterBehaviour elevator) {
            this.elevatorUI.Bind(elevator);
            this.elevatorUI.gameObject.SetActive(true);
        }

        public void HideElevatorUI() {
            this.elevatorUI.gameObject.SetActive(false);
        }

        public void ShowPropsContentUI(PropBehaviourBase behaviour) {
            this.propsContentUI.Setup(behaviour);
            this.propsContentUI.gameObject.SetActive(true);
        }

        public void HidePropsContentUI() {
            this.propsContentUI.gameObject.SetActive(false);
        }

        public void DisplayBuildPanel(bool isActive, BuildAreaConfig config = null, Action<CreateBuildingMessage> onCreate = null, Action onCancel = null) {
            this.buildPanelUI.gameObject.SetActive(isActive);

            if (isActive && config) {
                this.buildPanelUI.Setup(config.Buildings.First(), onCreate, onCancel);
            }
        }

        public void ToggleAdminPanel() {
            this.adminPanelManager.gameObject.SetActive(!this.adminPanelManager.gameObject.activeSelf);
        }
    }
}

