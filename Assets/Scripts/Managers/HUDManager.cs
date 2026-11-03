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

        [Header("Overlays")]
        [Tooltip("La barre d'accès rapide (Hotbar). Reparentée dans l'overlay inventaire quand il s'ouvre, remise à sa place à la fermeture.")]
        [SerializeField]
        private RectTransform hotbar;
        [Tooltip("Emplacement hôte de la Hotbar pendant que l'inventaire est ouvert (enfant plein écran du panneau inventaire).")]
        [SerializeField]
        private RectTransform inventoryHotbarSlot;
        [Tooltip("Éléments HUD de jeu (vitals, minimap, réticule, horloge, notifications…) masqués tant qu'un overlay plein écran est ouvert. NE PAS mettre Default View en entier (il héberge les modales) — seulement les éléments gameplay.")]
        [SerializeField]
        private GameObject[] gameHudElements;

        [SerializeField]
        private HelpPanel helpPanel;

        [Tooltip("Image-only panel shown while posing a held item (PanelTypeEnum.POSE).")]
        [SerializeField]
        private GameObject posePanel;

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
        private VehicleGarageUI vehicleGarageUI;

        [SerializeField]
        private AudioSource audioSource;

        [SerializeField]
        private AudioSource backgroundAudioSource;

        public static HUDManager Instance;

        private PanelTypeEnum currentPanelType;

        /// <summary>Gestion centralisée des overlays (exclusivité plein écran + masquage HUD de jeu + hotbar).</summary>
        public Sim.UI.HudOverlayManager Overlays { get; private set; }

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

            // Système d'overlays : exclusivité plein écran + masquage uniforme du HUD de jeu
            // + placement de la hotbar. Les overlays le notifient à l'ouverture/fermeture.
            this.Overlays = new Sim.UI.HudOverlayManager(this.gameHudElements, this.hotbar, this.inventoryHotbarSlot);

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

        private void Update() {
            if (SubGameController.IsActive) return;

            // Escape = FERMETURE UNIVERSELLE : ferme le dernier overlay/dialog/modale ouvert
            // (un seul par appui, par ordre de priorité). Point d'entrée UNIQUE — les overlays
            // ne lisent plus Escape eux-mêmes (sinon double-fermeture). Non inhibé par la
            // saisie de texte : Escape doit pouvoir fermer le chat/les champs eux-mêmes.
            if (Sim.Utils.GameInput.Down(UnityEngine.InputSystem.Key.Escape)) {
                if (this.CloseTopOverlay()) return;
            }

            // Raccourci global : I ouvre/ferme l'inventaire. Inhibé hors gameplay (pas de
            // joueur local = menu principal), en sub-game et pendant une saisie de texte.
            if (Sim.Utils.GameInput.Down(UnityEngine.InputSystem.Key.I)
                && PlayerController.Local != null
                && !Sim.Utils.CursorLock.IsTypingInInputField()) {
                this.ToggleInventory();
            }
        }

        /// <summary>Ferme le PREMIER overlay ouvert par ordre de priorité descendante (popup de
        /// saisie/confirmation &gt; chat &gt; modales plein écran &gt; constellation &gt; téléphone
        /// &gt; menu radial &gt; grille conteneur &gt; inventaire). Un seul par appui d'Escape.
        /// Source de vérité unique de la fermeture par Escape — aucun overlay ne lit Escape
        /// de son côté. Renvoie vrai si quelque chose a été fermé.</summary>
        private bool CloseTopOverlay() {
            // 1. Petites popups de saisie / confirmation (empilées au-dessus des modales).
            if (Close(SplitDialogUI.Instance, SplitDialogUI.Instance != null && SplitDialogUI.Instance.gameObject.activeSelf, () => SplitDialogUI.Instance.Hide())) return true;
            if (Close(ConfirmDialogUI.Instance, ConfirmDialogUI.Instance != null && ConfirmDialogUI.Instance.gameObject.activeSelf, () => ConfirmDialogUI.Instance.Hide())) return true;
            if (Close(DestroyConfirmUI.Instance, DestroyConfirmUI.Instance != null && DestroyConfirmUI.Instance.gameObject.activeSelf, () => DestroyConfirmUI.Instance.Hide())) return true;
            if (Close(BuyConfirmUI.Instance, BuyConfirmUI.Instance != null && BuyConfirmUI.Instance.gameObject.activeSelf, () => BuyConfirmUI.Instance.Hide())) return true;
            if (Close(GiveMoneyInputUI.Instance, GiveMoneyInputUI.Instance != null && GiveMoneyInputUI.Instance.gameObject.activeSelf, () => GiveMoneyInputUI.Instance.Hide())) return true;
            if (Close(SalePriceInputUI.Instance, SalePriceInputUI.Instance != null && SalePriceInputUI.Instance.gameObject.activeSelf, () => SalePriceInputUI.Instance.Hide())) return true;

            // 2. Saisie de chat.
            if (ChatInputUI.Instance != null && ChatInputUI.Instance.IsOpen) { ChatInputUI.Instance.Hide(); return true; }

            // 3. Modales plein écran.
            if (Close(DialogueUI.Instance, DialogueUI.Instance != null && DialogueUI.Instance.gameObject.activeSelf, () => DialogueUI.Instance.Hide())) return true;
            if (Close(MerchantShopUI.Instance, MerchantShopUI.Instance != null && MerchantShopUI.Instance.gameObject.activeSelf, () => MerchantShopUI.Instance.Hide())) return true;
            if (Close(VehicleShopUI.Instance, VehicleShopUI.Instance != null && VehicleShopUI.Instance.gameObject.activeSelf, () => VehicleShopUI.Instance.Hide())) return true;
            if (Close(LandParcelInfoUI.Instance, LandParcelInfoUI.Instance != null && LandParcelInfoUI.Instance.gameObject.activeSelf, () => LandParcelInfoUI.Instance.Hide())) return true;
            if (AtmUI.Instance != null && AtmUI.Instance.IsOpen) { AtmUI.Instance.Back(); return true; }
            if (this.vehicleGarageUI != null && this.vehicleGarageUI.gameObject.activeSelf) { this.vehicleGarageUI.Hide(); return true; }

            // 4. Constellation (carte de progression plein écran).
            if (this.constellationUI != null && this.constellationUI.IsOpen) { this.constellationUI.Close(); return true; }

            // 5. Téléphone.
            if (PhoneControllerUI.Instance != null && PhoneControllerUI.Instance.IsOpened) { PhoneControllerUI.Instance.ClosePhone(); return true; }

            // 6. Menu radial contextuel.
            if (this.IsContextMenuOpen) { this.CloseContextMenu(); return true; }

            // 7. Grille de conteneur / loot (colis tenu puis meuble/cadavre).
            if (ContainerPanelUI.Item != null && ContainerPanelUI.Item.IsDisplayed) { ContainerPanelUI.Item.Close(); return true; }
            if (ContainerPanelUI.Prop != null && ContainerPanelUI.Prop.IsDisplayed) { ContainerPanelUI.Prop.Close(); return true; }

            // 8. Inventaire.
            if (this.inventoryUI != null && this.inventoryUI.gameObject.activeSelf) { this.CloseInventory(); return true; }

            return false;
        }

        private static bool Close(Object inst, bool isOpen, System.Action close) {
            if (inst == null || !isOpen) return false;
            close();
            return true;
        }

        public void ShowVehicleHud(VehicleController vehicle, bool asDriver) {
            if (this.vehicleHudUI != null) this.vehicleHudUI.Show(vehicle, asDriver);
        }

        public void HideVehicleHud() {
            if (this.vehicleHudUI != null) this.vehicleHudUI.Hide();
        }

        /// <summary>Ouvre la modale du garage avec la liste des véhicules ; onPick(vehicleId) au choix.</summary>
        public void ShowGarage(System.Collections.Generic.List<VehicleGarageUI.Entry> entries, System.Action<string> onPick) {
            if (this.vehicleGarageUI != null) this.vehicleGarageUI.Show(entries, onPick);
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
                if (this.posePanel != null) this.posePanel.SetActive(false);
            } else if (panelType == PanelTypeEnum.POSE) {
                // Dedicated minimal "Mode Pose" HUD: just an image; no validate/cancel buttons
                // (confirm = click, cancel = Esc / right-click, handled by ItemPlacementController).
                if (this.posePanel != null) this.posePanel.SetActive(true);
                this.defaultViewUI.gameObject.SetActive(false);
                this.helpPanel.gameObject.SetActive(false);
                this.buildPreviewPanelUI.gameObject.SetActive(false);
            } else if (panelType == PanelTypeEnum.DEFAULT) {
                this.defaultViewUI.gameObject.SetActive(true);
                this.helpPanel.gameObject.SetActive(false);
                this.buildPreviewPanelUI.gameObject.SetActive(false);
                if (this.posePanel != null) this.posePanel.SetActive(false);
            } else {
                this.defaultViewUI.gameObject.SetActive(false);
                this.helpPanel.gameObject.SetActive(false);
                this.buildPreviewPanelUI.gameObject.SetActive(false);
                if (this.posePanel != null) this.posePanel.SetActive(false);
            }
        }

        public void ShowContextMenu(Action[] actions = null, Transform target = null, bool withPriority = false) {
            this.radialMenuUI.Setup(target, actions, withPriority);
        }

        public void CloseContextMenu() {
            this.radialMenuUI.Close();
        }

        /// <summary>True while the radial context menu is open (consumed by CursorLock in TPS mode).</summary>
        public bool IsContextMenuOpen => this.radialMenuUI != null && this.radialMenuUI.gameObject.activeSelf;

        public void ShowInventory() {
            this.inventoryUI.gameObject.SetActive(true);
        }

        public void ToggleInventory() {
            bool willOpen = !this.inventoryUI.gameObject.activeSelf;
            // L'overlay (exclusivité + masquage HUD + hotbar) est géré par le HudOverlayManager
            // via les notifications OnEnable/OnDisable de l'InventoryUI (IHudOverlay).
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