using System.Collections.Generic;
using AdvancedPeopleSystem;
using Mirror;
using Sim.Scriptables;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Cabine d'essayage d'une boutique de vêtements (<see cref="ClothingRackBehaviour"/>) —
    /// HUD DÉDIÉ plein écran (maquette boutique) : header + argent, onglets par catégorie,
    /// grille de cartes d'articles, panneau de détail (icône/nom/description/prix + « Essayer »),
    /// footer Total + Acheter/Annuler, drag souris = rotation du personnage.
    ///
    /// À l'ouverture : état DÉDIÉ <c>CharacterFitting</c> (verrouillé — seules sorties :
    /// Acheter/Annuler/Échap ou la mort), vcam de session (priorité 40) face au joueur, et
    /// VUE STUDIO : la caméra principale ne rend plus QUE le layer Player sur fond uni —
    /// aucun obstacle de vue possible (restauré à la fermeture). Le panneau est un overlay
    /// HidesGameHud (frère de « Default View » : stats/hotbar/minimap masqués).
    ///
    /// Cliquer une carte la SÉLECTIONNE (détail) ; « Essayer » la pose en preview sur le joueur
    /// (éléments APS2, un article par catégorie) et l'ajoute au panier ; « Acheter » envoie le
    /// panier au serveur (équipé direct / remplacement / poche / boîte de livraison).
    /// </summary>
    public class FittingRoomUI : MonoBehaviour, IHudOverlay {
        public OverlayLayer Layer => OverlayLayer.Fullscreen;
        // Le panneau est FRÈRE de « Default View » (pas enfant) : HidesGameHud peut donc
        // masquer toute la vue par défaut (vitals, hotbar, minimap, réticule) sans se masquer.
        public bool HidesGameHud => true;
        public bool ShowsHotbar  => false;
        public void CloseOverlay() => Hide();
        private void OnEnable()  => HudOverlayManager.Instance?.OnOpened(this);
        private void OnDisable() => HudOverlayManager.Instance?.OnClosed(this);

        [Header("Header (builder)")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text moneyLabel;

        [Header("Onglets + grille (builder)")]
        [SerializeField] private Transform tabsContainer;
        [SerializeField] private Button tabTemplate;
        [SerializeField] private Transform gridContainer;
        [SerializeField] private FittingRoomCard cardTemplate;

        [Header("Détail (builder)")]
        [SerializeField] private GameObject detailRoot;
        [SerializeField] private Image detailIcon;
        [SerializeField] private TMP_Text detailName;
        [SerializeField] private TMP_Text detailPrice;
        [SerializeField] private TMP_Text detailDescription;
        [SerializeField] private TMP_Text detailStats;
        [SerializeField] private Button tryButton;
        [SerializeField] private TMP_Text tryButtonLabel;

        [Header("Footer (builder)")]
        [SerializeField] private TMP_Text totalLabel;
        [SerializeField] private Button buyButton;
        [SerializeField] private TMP_Text buyButtonLabel;
        [SerializeField] private Button cancelButton;

        [Header("Vue studio")]
        [Tooltip("Distance caméra devant le joueur (m).")]
        [SerializeField] private float cameraDistance = 2.6f;
        [Tooltip("Hauteur caméra (m).")]
        [SerializeField] private float cameraHeight = 1.35f;
        [Tooltip("Hauteur du point visé sur le personnage (m).")]
        [SerializeField] private float lookAtHeight = 1.0f;
        [Tooltip("Fond uni de la vue studio (seul le layer Player est rendu).")]
        [SerializeField] private Color studioBackground = new Color(0.13f, 0.12f, 0.11f, 1f);
        [Tooltip("Vitesse de rotation du personnage au drag souris (°/unité d'axe).")]
        [SerializeField] private float rotateSpeed = 6f;

        public static FittingRoomUI Instance;

        private static readonly string[] TabNames = { "Chapeaux", "Accessoires", "Hauts", "Pantalons", "Chaussures" };

        private CharacterAppearance _appearance;
        private PlayerController _player;
        private Style _style;
        private GameObject _viewCamera;
        private bool _pendingPurchase;
        private int _currentTab = 2; // Hauts par défaut

        // Override caméra « studio » (restauré à la fermeture).
        private Camera _mainCamera;
        private int _prevCullingMask;
        private CameraClearFlags _prevClearFlags;
        private Color _prevBackground;

        // Catalogue filtré par genre, groupé par slot (0..4) ; panier : un article par catégorie.
        private readonly List<(ClothingConfig cfg, int price)>[] _entriesBySlot =
            new List<(ClothingConfig, int)>[ClothingConfig.WearSlotCount];
        private readonly Dictionary<CharacterElementType, (ClothingConfig cfg, int price)> _cart =
            new Dictionary<CharacterElementType, (ClothingConfig, int)>();
        private readonly List<FittingRoomCard> _cards = new List<FittingRoomCard>();
        private readonly List<Button> _tabs = new List<Button>();
        private FittingRoomCard _selectedCard;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.cardTemplate != null) this.cardTemplate.gameObject.SetActive(false);
            if (this.tabTemplate != null) this.tabTemplate.gameObject.SetActive(false);
            if (this.buyButton != null) this.buyButton.onClick.AddListener(this.OnBuyClicked);
            if (this.cancelButton != null) this.cancelButton.onClick.AddListener(this.Hide);
            if (this.tryButton != null) this.tryButton.onClick.AddListener(this.OnTryClicked);

            ClothingShopSystemBootstrap.OnPurchaseResult += this.OnPurchaseResult;
            PlayerBankAccount.OnLocalMoneyChanged += this.OnMoneyChanged;

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            ClothingShopSystemBootstrap.OnPurchaseResult -= this.OnPurchaseResult;
            PlayerBankAccount.OnLocalMoneyChanged -= this.OnMoneyChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>True tant que la cabine est ouverte — maintien de l'état CharacterFitting.</summary>
        public bool IsOpen => this.gameObject.activeSelf;

        // ── Ouverture ───────────────────────────────────────────────────────────────

        public void Open(ClothingRackBehaviour rack) {
            PlayerController player = PlayerController.Local;
            ClothingShopCatalogConfig catalog = rack != null ? rack.Catalog : null;
            if (player == null || catalog == null || this.IsOpen) return;

            this._player = player;
            this._appearance = player.GetComponent<CharacterAppearance>();
            this._style = player.CharacterData.Style;
            this._pendingPurchase = false;
            this._cart.Clear();
            this._selectedCard = null;

            if (this.titleLabel != null) this.titleLabel.text = catalog.ShopLabel;
            this.BuildEntries(catalog, this._style.gender);
            this.BuildTabs();
            this.ShowTab(this._currentTab);
            this.ShowDetail(null);
            this.RefreshCartUI();

            // État CABINE dédié avant l'ouverture de l'UI (la sortie d'Idle referme
            // l'inventaire) : verrouillé — seule la fermeture du panneau ou la mort en sort.
            player.EnterFitting();

            // Vcam du côté opposé au portant + joueur pivoté face caméra, puis VUE STUDIO :
            // la caméra principale ne rend plus que le layer Player sur fond uni.
            Vector3 awayDir = player.transform.position - rack.transform.position;
            awayDir.y = 0f;
            awayDir = awayDir.sqrMagnitude > 0.01f ? awayDir.normalized : -player.transform.forward;
            this.SpawnViewCamera(player.transform, awayDir);
            if (this._viewCamera != null) player.LookAt(this._viewCamera.transform);
            this.ApplyStudioRendering();

            this.gameObject.SetActive(true);
        }

        // ── Catalogue / onglets / grille ───────────────────────────────────────────

        private void BuildEntries(ClothingShopCatalogConfig catalog, Gender gender) {
            for (int slot = 0; slot < this._entriesBySlot.Length; slot++) {
                this._entriesBySlot[slot] = new List<(ClothingConfig, int)>();
            }
            foreach (ItemPrice entry in catalog.Entries) {
                if (entry.item is ClothingConfig clothing && clothing.SupportsGender(gender)) {
                    this._entriesBySlot[clothing.WearSlotIndex].Add((clothing, entry.price));
                }
            }
            foreach (var list in this._entriesBySlot) {
                list.Sort((a, b) => string.Compare(a.Item1.Label, b.Item1.Label, System.StringComparison.Ordinal));
            }
        }

        private void BuildTabs() {
            if (this.tabTemplate == null || this.tabsContainer == null) return;
            foreach (Button tab in this._tabs) {
                if (tab != null) Destroy(tab.gameObject);
            }
            this._tabs.Clear();

            for (int slot = 0; slot < TabNames.Length; slot++) {
                int captured = slot;
                Button tab = Instantiate(this.tabTemplate, this.tabsContainer);
                tab.gameObject.SetActive(true);
                TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = TabNames[slot];
                ApplyTabIcon(tab, this._entriesBySlot[slot]);
                tab.onClick.AddListener(() => this.ShowTab(captured));
                this._tabs.Add(tab);
            }
        }

        /// <summary>Icône de catégorie de l'onglet (enfant « Icon » du template) : celle du
        /// premier article de la catégorie — représentatif et sans asset dédié.</summary>
        private static void ApplyTabIcon(Button tab, List<(ClothingConfig cfg, int price)> entries) {
            Transform iconTransform = tab.transform.Find("Icon");
            Image icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            if (icon == null) return;
            Sprite sprite = entries != null && entries.Count > 0 ? entries[0].cfg.Icon : null;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        private void ShowTab(int slot) {
            this._currentTab = Mathf.Clamp(slot, 0, this._entriesBySlot.Length - 1);

            for (int i = 0; i < this._tabs.Count; i++) {
                Image bg = this._tabs[i] != null ? this._tabs[i].GetComponent<Image>() : null;
                if (bg != null) {
                    bg.color = i == this._currentTab
                        ? new Color(0.22f, 0.42f, 0.65f, 1f)
                        : new Color(0.16f, 0.17f, 0.21f, 1f);
                }
            }

            foreach (FittingRoomCard card in this._cards) {
                if (card != null) Destroy(card.gameObject);
            }
            this._cards.Clear();
            this._selectedCard = null;
            this.ShowDetail(null);

            if (this.cardTemplate == null || this.gridContainer == null) return;
            foreach ((ClothingConfig cfg, int price) in this._entriesBySlot[this._currentTab]) {
                FittingRoomCard card = Instantiate(this.cardTemplate, this.gridContainer);
                card.gameObject.SetActive(true);
                card.Bind(cfg, price, this.OnCardClicked);
                card.SetInCart(this.IsInCart(cfg));
                this._cards.Add(card);
            }
        }

        private bool IsInCart(ClothingConfig cfg) {
            return this._cart.TryGetValue(cfg.ElementType, out var entry) && entry.cfg == cfg;
        }

        // ── Sélection + détail ─────────────────────────────────────────────────────

        private void OnCardClicked(FittingRoomCard card) {
            if (this._pendingPurchase) return;
            if (this._selectedCard != null) this._selectedCard.SetSelected(false);
            this._selectedCard = card;
            card.SetSelected(true);
            this.ShowDetail(card);
        }

        private void ShowDetail(FittingRoomCard card) {
            bool visible = card != null && card.Clothing != null;
            if (this.detailRoot != null) this.detailRoot.SetActive(visible);
            if (!visible) return;

            ClothingConfig cfg = card.Clothing;
            if (this.detailIcon != null) {
                this.detailIcon.sprite = cfg.Icon;
                this.detailIcon.enabled = cfg.Icon != null;
            }
            if (this.detailName != null) this.detailName.text = cfg.Label;
            if (this.detailPrice != null) this.detailPrice.text = $"{card.Price} $";
            if (this.detailDescription != null) this.detailDescription.text = cfg.Description;
            if (this.detailStats != null) this.detailStats.text = StatsText(cfg);
            if (this.tryButtonLabel != null) {
                this.tryButtonLabel.text = this.IsInCart(cfg) ? "Retirer" : "Essayer";
            }
        }

        /// <summary>Caractéristiques affichées dans le panneau de détail. Les futures
        /// caractéristiques par instance (rareté, stats, couleurs — stateData) s'ajouteront ici.</summary>
        public static string StatsText(ClothingConfig cfg) {
            int slot = Mathf.Clamp(cfg.WearSlotIndex, 0, TabNames.Length - 1);
            string genre = cfg.Gender == Gender.MALE ? "Homme" : "Femme";
            return $"Catégorie : {TabNames[slot]}\nGenre : {genre}";
        }

        // ── Panier + preview ───────────────────────────────────────────────────────

        private void OnTryClicked() {
            if (this._pendingPurchase || this._selectedCard == null || this._selectedCard.Clothing == null) return;

            ClothingConfig cfg = this._selectedCard.Clothing;
            CharacterElementType type = cfg.ElementType;
            if (this.IsInCart(cfg)) {
                this._cart.Remove(type);
            } else {
                this._cart[type] = (cfg, this._selectedCard.Price);
            }

            this.ApplyPreview();
            this.RefreshCardMarkers();
            this.ShowDetail(this._selectedCard);
            this.RefreshCartUI();
        }

        private void RefreshCardMarkers() {
            foreach (FittingRoomCard card in this._cards) {
                if (card != null && card.Clothing != null) card.SetInCart(this.IsInCart(card.Clothing));
            }
        }

        /// <summary>Restaure le vrai look puis pose les articles du panier par-dessus (éléments
        /// APS2 directs — purement local, rien n'est répliqué pendant l'essayage).</summary>
        private void ApplyPreview() {
            if (this._appearance == null) return;
            this._appearance.ReapplyLook(this._style);
            foreach ((ClothingConfig cfg, int _) in this._cart.Values) {
                this._appearance.SetElement(cfg.ElementType, cfg.ElementIndex);
            }
        }

        private void RefreshCartUI() {
            int total = 0;
            foreach ((ClothingConfig _, int price) in this._cart.Values) total += price;

            int money = LocalMoney();
            if (this.moneyLabel != null) this.moneyLabel.text = $"{money} $";
            if (this.totalLabel != null) this.totalLabel.text = $"Total : {total} $";
            if (this.buyButtonLabel != null) {
                this.buyButtonLabel.text = this._cart.Count > 0 ? $"Acheter ({this._cart.Count})" : "Acheter";
            }
            if (this.buyButton != null) {
                this.buyButton.interactable = !this._pendingPurchase && this._cart.Count > 0 && money >= total;
            }
        }

        private void OnMoneyChanged(int _) {
            if (this.IsOpen) this.RefreshCartUI();
        }

        // ── Rotation du personnage (drag souris hors UI) ───────────────────────────

        private void Update() {
            if (this._player == null || this._pendingPurchase) return;
            if (!UnityEngine.Input.GetMouseButton(0)) return;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.IsPointerOverGameObject()) return;

            float deltaX = UnityEngine.Input.GetAxis("Mouse X");
            if (Mathf.Abs(deltaX) > 0.0001f) {
                this._player.transform.Rotate(0f, -deltaX * this.rotateSpeed, 0f, Space.World);
            }
        }

        // ── Achat ───────────────────────────────────────────────────────────────────

        private void OnBuyClicked() {
            if (this._pendingPurchase || this._cart.Count == 0 || !NetworkClient.isConnected) return;

            var ids = new int[this._cart.Count];
            int i = 0;
            foreach ((ClothingConfig cfg, int _) in this._cart.Values) ids[i++] = cfg.ID;

            this._pendingPurchase = true;
            this.RefreshCartUI();
            NetworkClient.Send(new C2S_ClothingShopPurchase { ConfigIds = ids });
        }

        private void OnPurchaseResult(bool success, byte reason, int totalPaid) {
            if (!this.IsOpen) return;

            if (success) {
                WorldToastManager.ShowSuccess($"Achat effectué ({totalPaid} $)");
                // La fermeture restaure le look local ; la SyncVar equippedClothing (recalculée
                // par le serveur) applique les vêtements réellement équipés dans la foulée.
                this.Hide();
                return;
            }

            this._pendingPurchase = false;
            this.RefreshCartUI();
            WorldToastManager.ShowError(ReasonText(reason));
        }

        private static string ReasonText(byte reason) {
            switch (reason) {
                case ClothingShopSystemBootstrap.ReasonUnavailable: return "Article indisponible.";
                case ClothingShopSystemBootstrap.ReasonFunds:       return "Fonds insuffisants.";
                case ClothingShopSystemBootstrap.ReasonNoSpace:     return InventoryToasts.NoSpaceInInventory + ".";
                case ClothingShopSystemBootstrap.ReasonTooFar:      return "Trop loin de la boutique.";
                default:                                            return "Achat impossible.";
            }
        }

        // ── Fermeture ───────────────────────────────────────────────────────────────

        public void Hide() {
            this.RestoreStudioRendering();
            this.DestroyViewCamera();
            if (this._appearance != null) this._appearance.ReapplyLook(this._style);

            foreach (FittingRoomCard card in this._cards) {
                if (card != null) Destroy(card.gameObject);
            }
            this._cards.Clear();
            foreach (Button tab in this._tabs) {
                if (tab != null) Destroy(tab.gameObject);
            }
            this._tabs.Clear();

            this._cart.Clear();
            this._selectedCard = null;
            this._pendingPurchase = false;
            this._appearance = null;
            this._player = null;
            // Désactivation → OnClosed (HUD de jeu restauré) ; l'état CharacterFitting repasse
            // Idle à son Tick suivant (seule sortie normale de l'état).
            this.gameObject.SetActive(false);
        }

        // ── Vue studio (caméra) ─────────────────────────────────────────────────────

        /// <summary>Vcam temporaire (priorité 40 &gt; TPS) posée dans <paramref name="awayDir"/>
        /// (côté opposé au portant), tournée vers le joueur — le brain fait le blend.</summary>
        private void SpawnViewCamera(Transform player, Vector3 awayDir) {
            this.DestroyViewCamera();

            Vector3 lookTarget = player.position + Vector3.up * this.lookAtHeight;
            Vector3 position = player.position + awayDir * this.cameraDistance + Vector3.up * this.cameraHeight;

            this._viewCamera = new GameObject("Fitting Room Camera");
            this._viewCamera.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(lookTarget - position));
            CinemachineCamera cam = this._viewCamera.AddComponent<CinemachineCamera>();
            cam.Priority = 40;
        }

        private void DestroyViewCamera() {
            if (this._viewCamera != null) {
                Destroy(this._viewCamera);
                this._viewCamera = null;
            }
        }

        /// <summary>VUE STUDIO : la caméra principale ne rend plus QUE le layer Player, sur un
        /// fond uni — tous les renderers du rig APS2 vivent sur ce layer, et plus RIEN ne peut
        /// obstruer la vue (portants, murs, vitrines…). Restauré par
        /// <see cref="RestoreStudioRendering"/> à la fermeture.</summary>
        private void ApplyStudioRendering() {
            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
            if (cam == null) return;

            this._mainCamera = cam;
            this._prevCullingMask = cam.cullingMask;
            this._prevClearFlags = cam.clearFlags;
            this._prevBackground = cam.backgroundColor;

            cam.cullingMask = LayerMask.GetMask("Player");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = this.studioBackground;
        }

        private void RestoreStudioRendering() {
            if (this._mainCamera == null) return;
            this._mainCamera.cullingMask = this._prevCullingMask;
            this._mainCamera.clearFlags = this._prevClearFlags;
            this._mainCamera.backgroundColor = this._prevBackground;
            this._mainCamera = null;
        }

        private static int LocalMoney() {
            PlayerController local = PlayerController.Local;
            PlayerBankAccount bank = local != null ? local.GetComponent<PlayerBankAccount>() : null;
            return bank != null ? bank.Money : 0;
        }
    }
}
