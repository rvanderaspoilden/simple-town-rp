using Sim.Interactables;
using Sim.UI;
using UnityEngine;

namespace Sim {
    public class HUDManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private BuildPreviewPanelUI buildPreviewPanelUI;

        [SerializeField]
        private RadialMenuUI radialMenuUI;

        [SerializeField]
        private InventoryUI inventoryUI;

        [SerializeField]
        private DefaultViewUI defaultViewUI;

        [SerializeField]
        private HelpPanel helpPanel;

        [SerializeField]
        private AudioSource audioSource;

        [SerializeField]
        private AudioSource backgroundAudioSource;

        public static HUDManager Instance;

        private PanelTypeEnum currentPanelType;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
                this.audioSource = GetComponent<AudioSource>();
            }

            DontDestroyOnLoad(this.gameObject);
        }

        // Start is called before the first frame update
        void Start() {
            this.DisplayPanel(PanelTypeEnum.NONE);
            this.CloseContextMenu();
            this.CloseInventory();
        }

        public void PlaySound(AudioClip sound, float volume) {
            this.audioSource.volume = volume;
            this.audioSource.PlayOneShot(sound);
        }

        public void PlayBackgroundSound(AudioClip audioClip, float volume) {
            this.backgroundAudioSource.clip = audioClip;
            this.backgroundAudioSource.volume = volume;
            this.backgroundAudioSource.Play();
        }

        public void StopBackgroundSound() {
            this.backgroundAudioSource.Stop();
        }

        public void StopSound() {
            this.audioSource.Stop();
        }

        public void DisplayPanel(PanelTypeEnum panelType) {
            if (panelType == PanelTypeEnum.BUILD) {
                this.buildPreviewPanelUI.gameObject.SetActive(true);
                this.helpPanel.gameObject.SetActive(true);
                this.defaultViewUI.gameObject.SetActive(false);
            } else if (panelType == PanelTypeEnum.DEFAULT) {
                this.defaultViewUI.gameObject.SetActive(true);
                this.helpPanel.gameObject.SetActive(false);
                this.buildPreviewPanelUI.gameObject.SetActive(false);
            } else {
                this.defaultViewUI.gameObject.SetActive(false);
                this.helpPanel.gameObject.SetActive(false);
                this.buildPreviewPanelUI.gameObject.SetActive(false);
            }
        }

        public void ShowContextMenu(Action[] actions = null, Transform target = null, bool withPriority = false) {
            this.radialMenuUI.Setup(target, actions, withPriority);
        }

        public void CloseContextMenu() {
            this.radialMenuUI.Close();
        }

        public void ShowInventory() {
            this.inventoryUI.gameObject.SetActive(true);
        }

        public void ToggleInventory() {
            this.inventoryUI.gameObject.SetActive(!this.inventoryUI.gameObject.activeSelf);
        }

        public InventoryUI InventoryUI => inventoryUI;

        public void CloseInventory() {
            this.inventoryUI.gameObject.SetActive(false);
        }
    }
}