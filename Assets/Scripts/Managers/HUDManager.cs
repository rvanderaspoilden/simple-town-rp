using Sim.Constellation;
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
        private ChatInputUI chatInputUI;

        [SerializeField]
        private SalePriceInputUI salePriceInputUI;

        [SerializeField]
        private BuyConfirmUI buyConfirmUI;

        [SerializeField]
        private ConstellationUI constellationUI;

        [SerializeField]
        private VehicleHudUI vehicleHudUI;

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

                // Route la musique de fond vers le groupe Music du mixer → contrôlée par le
                // slider Musique des réglages (le slider SFX/Master n'affecte pas la musique).
                if (this.backgroundAudioSource != null) {
                    var mixer = Resources.Load<UnityEngine.Audio.AudioMixer>("Audio/GameAudio");
                    if (mixer != null) {
                        var music = mixer.FindMatchingGroups("Music");
                        if (music.Length > 0) this.backgroundAudioSource.outputAudioMixerGroup = music[0];
                    }
                }
            }

            DontDestroyOnLoad(this.gameObject);
        }

        // Start is called before the first frame update
        void Start() {
            this.DisplayPanel(PanelTypeEnum.NONE);
            this.CloseContextMenu();
            this.CloseInventory();
            this.CloseConstellation();
            this.HideVehicleHud();
        }

        public void ShowVehicleHud(VehicleController vehicle, bool asDriver) {
            if (this.vehicleHudUI != null) this.vehicleHudUI.Show(vehicle, asDriver);
        }

        public void HideVehicleHud() {
            if (this.vehicleHudUI != null) this.vehicleHudUI.Hide();
        }

        // Façade : tous les appels existants passent désormais par l'AudioManager (pooling +
        // mixer centralisés). Signature conservée → aucun site d'appel à modifier.
        public void PlaySound(AudioClip sound, float volume) {
            Sim.Audio.AudioManager.Instance.PlayClip2D(sound, volume);
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
            bool willOpen = !this.inventoryUI.gameObject.activeSelf;
            this.inventoryUI.gameObject.SetActive(willOpen);
            Sim.Audio.AudioManager.Instance.PlayUI(
                willOpen ? Sim.Audio.SfxId.InventoryOpen : Sim.Audio.SfxId.InventoryClose);
        }

        public InventoryUI InventoryUI => inventoryUI;

        public void CloseInventory() {
            this.inventoryUI.gameObject.SetActive(false);
        }

        public SalePriceInputUI SalePriceInputUI => salePriceInputUI;

        public BuyConfirmUI BuyConfirmUI => buyConfirmUI;

        public ConstellationUI ConstellationUI => constellationUI;

        public void ShowConstellation() {
            if (this.constellationUI != null) this.constellationUI.gameObject.SetActive(true);
        }

        public void CloseConstellation() {
            if (this.constellationUI != null) this.constellationUI.gameObject.SetActive(false);
        }

        public void ToggleConstellation() {
            if (this.constellationUI != null)
                this.constellationUI.gameObject.SetActive(!this.constellationUI.gameObject.activeSelf);
        }

        public void ShowChatInput() {
            if (this.chatInputUI != null && !this.chatInputUI.IsOpen) this.chatInputUI.Show();
        }

        public void ToggleChatInput() {
            if (this.chatInputUI != null) this.chatInputUI.Toggle();
        }
    }
}