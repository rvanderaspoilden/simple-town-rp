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
    /// Cabine d'essayage d'une boutique (<see cref="ClothingRackBehaviour"/>) — HUD DÉDIÉ plein
    /// écran : header + argent, onglets par catégorie, grille de cartes d'articles, panneau de
    /// détail (icône/nom/description/prix + « Essayer »), bandeau « Panier », footer Total +
    /// Acheter/Annuler, drag souris = rotation du personnage.
    ///
    /// GÉNÉRIQUE : le portant porte un catalogue (<see cref="ClothingShopCatalogConfig"/>, liste
    /// d'items + prix) ; tout item implémentant <see cref="IApsWearable"/> est essayable
    /// (vêtements <see cref="ClothingConfig"/>, sacs <see cref="BackpackConfig"/>…). Les
    /// ONGLETS sont déduits des catégories APS2 réellement présentes dans le catalogue (filtré
    /// par le genre du personnage) — deux portants peuvent donc vendre des choses différentes.
    ///
    /// À l'ouverture : état DÉDIÉ <c>CharacterFitting</c> (verrouillé — seules sorties :
    /// Acheter/Annuler/Échap ou la mort), vcam de session (priorité 40) face au joueur, et
    /// VUE STUDIO : les renderers du rig du joueur passent sur le layer dédié « Fitting Preview »
    /// et la caméra principale ne rend plus QUE ce layer sur fond uni — aucun obstacle de vue
    /// possible, ni décor ni autre personnage (restauré à la fermeture). Le panneau est un overlay
    /// HidesGameHud (frère de « Default View » : stats/hotbar/minimap masqués).
    ///
    /// Cliquer une carte la SÉLECTIONNE (détail) ; « Essayer » la pose en preview sur le joueur
    /// (éléments APS2, un article par catégorie) et l'ajoute au panier ; « Acheter » envoie le
    /// panier au serveur (vêtement : équipé direct / remplacement ; sac : poche, sinon boîte).
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

        [Header("Panier (builder)")]
        [Tooltip("Conteneur des vignettes du panier (HorizontalLayoutGroup).")]
        [SerializeField] private Transform cartItemsContainer;
        [Tooltip("Template désactivé d'une vignette du panier, cloné par article.")]
        [SerializeField] private FittingRoomCartItem cartItemTemplate;
        [Tooltip("Libellé affiché quand le panier est vide.")]
        [SerializeField] private TMP_Text cartEmptyLabel;

        [Header("Vue studio")]
        // Cadrage validé au play-test 2026-11-14 (vcam FOV par défaut) : corps ENTIER visible,
        // pieds compris — à 2,6 m les chevilles sortaient du cadre.
        [Tooltip("Distance caméra devant le joueur (m).")]
        [SerializeField] private float cameraDistance = 3.2f;
        [Tooltip("Hauteur caméra (m).")]
        [SerializeField] private float cameraHeight = 1.25f;
        [Tooltip("Hauteur du point visé sur le personnage (m).")]
        [SerializeField] private float lookAtHeight = 0.95f;
        [Tooltip("Fond uni de la vue studio (seul le layer « Fitting Preview » est rendu).")]
        [SerializeField] private Color studioBackground = new Color(0.13f, 0.12f, 0.11f, 1f);
        [Tooltip("Vitesse de rotation du personnage au drag souris (°/unité d'axe).")]
        [SerializeField] private float rotateSpeed = 6f;

        public static FittingRoomUI Instance;

        /// <summary>Ordre d'affichage des onglets. Seules les catégories PRÉSENTES dans le
        /// catalogue du portant (après filtre de genre) produisent un onglet.</summary>
        public static readonly CharacterElementType[] TabOrder = {
            CharacterElementType.Hat, CharacterElementType.Accessory, CharacterElementType.Shirt,
            CharacterElementType.Pants, CharacterElementType.Shoes, CharacterElementType.Item1,
        };

        /// <summary>Libellé FR d'un onglet / d'une catégorie essayable.</summary>
        public static string TabName(CharacterElementType type) {
            switch (type) {
                case CharacterElementType.Hat:       return "Chapeaux";
                case CharacterElementType.Accessory: return "Accessoires";
                case CharacterElementType.Shirt:     return "Hauts";
                case CharacterElementType.Pants:     return "Pantalons";
                case CharacterElementType.Shoes:     return "Chaussures";
                case CharacterElementType.Item1:     return "Sacs";
                default:                             return type.ToString();
            }
        }

        private CharacterAppearance _appearance;
        private PlayerController _player;
        private Style _style;
        private GameObject _viewCamera;
        private bool _pendingPurchase;
        private CharacterElementType _currentType = CharacterElementType.Shirt; // Hauts par défaut

        // Override caméra « studio » (restauré à la fermeture).
        private Camera _mainCamera;
        private int _prevCullingMask;
        private CameraClearFlags _prevClearFlags;
        private Color _prevBackground;

        /// <summary>Layer DÉDIÉ à la vue studio (TagManager, index 29) : personne d'autre ne vit
        /// dessus, donc rien d'autre que le rig du joueur local ne peut apparaître — ni décor, ni
        /// autre joueur/NPC, quel que soit le layer d'origine des meshes (les meshes APS2 sont
        /// instanciés sur Default). Repli sur « Player » si le layer manque.</summary>
        private const string PreviewLayerName = "Fitting Preview";
        private int _previewLayer = -1;
        // Layers d'origine des renderers du rig, restaurés à la fermeture.
        private readonly List<(GameObject go, int layer)> _previewLayerBackup = new List<(GameObject, int)>();

        // Catalogue filtré par genre, groupé par catégorie APS2 ; onglets = catégories présentes
        // (ordre TabOrder) ; panier : un article par catégorie.
        private readonly Dictionary<CharacterElementType, List<(ItemConfig cfg, int price)>> _entriesByType =
            new Dictionary<CharacterElementType, List<(ItemConfig, int)>>();
        private readonly List<CharacterElementType> _tabTypes = new List<CharacterElementType>();
        private readonly Dictionary<CharacterElementType, (ItemConfig cfg, int price)> _cart =
            new Dictionary<CharacterElementType, (ItemConfig, int)>();
        private readonly List<FittingRoomCard> _cards = new List<FittingRoomCard>();
        private readonly List<Button> _tabs = new List<Button>();
        private readonly List<FittingRoomCartItem> _cartItems = new List<FittingRoomCartItem>();
        private FittingRoomCard _selectedCard;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.cardTemplate != null) this.cardTemplate.gameObject.SetActive(false);
            if (this.tabTemplate != null) this.tabTemplate.gameObject.SetActive(false);
            if (this.cartItemTemplate != null) this.cartItemTemplate.gameObject.SetActive(false);
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
            if (this._tabTypes.Count == 0) {
                WorldToastManager.ShowError("Rien à votre taille dans cette boutique.");
                return;
            }
            this.BuildTabs();
            this.ShowTab(this._tabTypes.Contains(this._currentType) ? this._currentType : this._tabTypes[0]);
            this.ShowDetail(null);
            this.RefreshCartUI();

            // État CABINE dédié avant l'ouverture de l'UI (la sortie d'Idle referme
            // l'inventaire) : verrouillé — seule la fermeture du panneau ou la mort en sort.
            player.EnterFitting();

            // Vcam du côté opposé au portant + joueur pivoté face caméra, puis VUE STUDIO.
            Vector3 awayDir = player.transform.position - rack.transform.position;
            awayDir.y = 0f;
            awayDir = awayDir.sqrMagnitude > 0.01f ? awayDir.normalized : -player.transform.forward;
            this.SpawnViewCamera(player.transform, awayDir);
            if (this._viewCamera != null) player.LookAt(this._viewCamera.transform);
            this.ApplyStudioRendering();

            this.gameObject.SetActive(true);
        }

        // ── Catalogue / onglets / grille ───────────────────────────────────────────

        /// <summary>Groupe le catalogue du portant par catégorie APS2 (items
        /// <see cref="IApsWearable"/> portables par le genre du personnage) et dérive la liste
        /// des onglets (catégories présentes, ordre <see cref="TabOrder"/>).</summary>
        private void BuildEntries(ClothingShopCatalogConfig catalog, Gender gender) {
            this._entriesByType.Clear();
            this._tabTypes.Clear();

            foreach (ItemPrice entry in catalog.Entries) {
                if (!(entry.item is IApsWearable wearable) || !wearable.SupportsGender(gender)) continue;
                if (!this._entriesByType.TryGetValue(wearable.ElementType, out var list)) {
                    list = new List<(ItemConfig, int)>();
                    this._entriesByType[wearable.ElementType] = list;
                }
                list.Add((entry.item, entry.price));
            }
            foreach (var list in this._entriesByType.Values) {
                list.Sort((a, b) => string.Compare(a.cfg.Label, b.cfg.Label, System.StringComparison.Ordinal));
            }
            foreach (CharacterElementType type in TabOrder) {
                if (this._entriesByType.ContainsKey(type)) this._tabTypes.Add(type);
            }
        }

        private void BuildTabs() {
            if (this.tabTemplate == null || this.tabsContainer == null) return;
            foreach (Button tab in this._tabs) {
                if (tab != null) Destroy(tab.gameObject);
            }
            this._tabs.Clear();

            foreach (CharacterElementType type in this._tabTypes) {
                CharacterElementType captured = type;
                Button tab = Instantiate(this.tabTemplate, this.tabsContainer);
                tab.gameObject.SetActive(true);
                TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = TabName(type);
                ApplyTabIcon(tab, this._entriesByType[type]);
                tab.onClick.AddListener(() => this.ShowTab(captured));
                this._tabs.Add(tab);
            }
        }

        /// <summary>Icône de catégorie de l'onglet (enfant « Icon » du template) : celle du
        /// premier article de la catégorie — représentatif et sans asset dédié.</summary>
        private static void ApplyTabIcon(Button tab, List<(ItemConfig cfg, int price)> entries) {
            Transform iconTransform = tab.transform.Find("Icon");
            Image icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            if (icon == null) return;
            Sprite sprite = entries != null && entries.Count > 0 ? entries[0].cfg.Icon : null;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        private void ShowTab(CharacterElementType type) {
            if (!this._entriesByType.ContainsKey(type)) return;
            this._currentType = type;

            for (int i = 0; i < this._tabs.Count; i++) {
                Image bg = this._tabs[i] != null ? this._tabs[i].GetComponent<Image>() : null;
                if (bg != null) {
                    bg.color = this._tabTypes[i] == this._currentType
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
            foreach ((ItemConfig cfg, int price) in this._entriesByType[this._currentType]) {
                FittingRoomCard card = Instantiate(this.cardTemplate, this.gridContainer);
                card.gameObject.SetActive(true);
                card.Bind(cfg, price, this.OnCardClicked);
                card.SetInCart(this.IsInCart(cfg));
                card.SetWorn(this.IsWorn(cfg));
                this._cards.Add(card);
            }
        }

        private bool IsInCart(ItemConfig cfg) {
            return cfg is IApsWearable w && this._cart.TryGetValue(w.ElementType, out var entry) && entry.cfg == cfg;
        }

        /// <summary>L'article est-il celui que le personnage porte RÉELLEMENT (pas la preview) ?
        /// Vêtement : garde-robe répliquée ; sac : item du slot dos. Pilote le badge « déjà
        /// porté » des cartes et la mention du détail.</summary>
        private bool IsWorn(ItemConfig cfg) {
            if (this._player == null || cfg == null) return false;
            if (cfg is ClothingConfig) return this._player.IsWearingClothing(cfg.ID);
            if (cfg is BackpackConfig) {
                PlayerHands hands = this._player.GetComponent<PlayerHands>();
                return hands != null && hands.BackItem != null && hands.BackItem.Configuration == cfg;
            }
            return false;
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
            bool visible = card != null && card.Item != null;
            if (this.detailRoot != null) this.detailRoot.SetActive(visible);
            if (!visible) return;

            ItemConfig cfg = card.Item;
            if (this.detailIcon != null) {
                this.detailIcon.sprite = cfg.Icon;
                this.detailIcon.enabled = cfg.Icon != null;
            }
            if (this.detailName != null) this.detailName.text = cfg.Label;
            if (this.detailPrice != null) this.detailPrice.text = $"{card.Price} $";
            if (this.detailDescription != null) this.detailDescription.text = cfg.Description;
            if (this.detailStats != null) {
                this.detailStats.text = this.IsWorn(cfg)
                    ? StatsText(cfg) + "\n<color=#8FD3FF>Déjà porté par votre personnage</color>"
                    : StatsText(cfg);
            }
            if (this.tryButtonLabel != null) {
                this.tryButtonLabel.text = this.IsInCart(cfg) ? "Retirer" : "Essayer";
            }
        }

        /// <summary>Caractéristiques affichées dans le panneau de détail : catégorie, puis genre
        /// (vêtement) ou capacité (sac). Les futures caractéristiques par instance (rareté,
        /// couleurs — stateData) s'ajouteront ici.</summary>
        public static string StatsText(ItemConfig cfg) {
            string category = cfg is IApsWearable w ? TabName(w.ElementType) : "—";
            string text = $"Catégorie : {category}";
            if (cfg is ClothingConfig clothing) {
                text += $"\nGenre : {(clothing.Gender == Gender.MALE ? "Homme" : "Femme")}";
            }
            if (cfg is BackpackConfig backpack) {
                text += $"\nCapacité : {backpack.Container.SlotCount} emplacements\nMixte";
            }
            return text;
        }

        // ── Panier + preview ───────────────────────────────────────────────────────

        private void OnTryClicked() {
            if (this._pendingPurchase || this._selectedCard == null || this._selectedCard.Item == null) return;

            ItemConfig cfg = this._selectedCard.Item;
            if (!(cfg is IApsWearable wearable)) return;
            if (this.IsInCart(cfg)) {
                this._cart.Remove(wearable.ElementType);
            } else {
                this._cart[wearable.ElementType] = (cfg, this._selectedCard.Price);
            }

            this.ApplyPreview();
            this.RefreshCardMarkers();
            this.ShowDetail(this._selectedCard);
            this.RefreshCartUI();
        }

        private void RefreshCardMarkers() {
            foreach (FittingRoomCard card in this._cards) {
                if (card != null && card.Item != null) card.SetInCart(this.IsInCart(card.Item));
            }
        }

        /// <summary>Croix d'une vignette du bandeau « Panier » : retire l'article du panier et de
        /// la preview, quel que soit l'onglet affiché. Le détail (Essayer/Retirer) et les coches
        /// des cartes visibles suivent.</summary>
        private void OnCartItemRemoveClicked(FittingRoomCartItem item) {
            if (this._pendingPurchase || item == null || item.Item == null) return;
            if (!this.IsInCart(item.Item) || !(item.Item is IApsWearable wearable)) return;

            this._cart.Remove(wearable.ElementType);
            this.ApplyPreview();
            this.RefreshCardMarkers();
            this.ShowDetail(this._selectedCard);
            this.RefreshCartUI();
        }

        /// <summary>Reconstruit les vignettes du bandeau « Panier » dans l'ordre des onglets
        /// (chapeau → sac) ; libellé « Panier vide » sinon.</summary>
        private void RebuildCartStrip() {
            foreach (FittingRoomCartItem item in this._cartItems) {
                if (item != null) Destroy(item.gameObject);
            }
            this._cartItems.Clear();

            if (this.cartEmptyLabel != null) this.cartEmptyLabel.gameObject.SetActive(this._cart.Count == 0);
            if (this.cartItemTemplate == null || this.cartItemsContainer == null) return;

            foreach (CharacterElementType type in TabOrder) {
                if (!this._cart.TryGetValue(type, out var entry)) continue;
                FittingRoomCartItem item = Instantiate(this.cartItemTemplate, this.cartItemsContainer);
                item.gameObject.SetActive(true);
                item.Bind(entry.cfg, entry.price, this.OnCartItemRemoveClicked);
                item.SetRemovable(!this._pendingPurchase);
                this._cartItems.Add(item);
            }
        }

        /// <summary>Restaure le vrai look puis pose les articles du panier par-dessus (éléments
        /// APS2 directs — purement local, rien n'est répliqué pendant l'essayage).</summary>
        private void ApplyPreview() {
            if (this._appearance == null) return;
            this._appearance.ReapplyLook(this._style);
            foreach ((ItemConfig cfg, int _) in this._cart.Values) {
                if (cfg is IApsWearable wearable) {
                    this._appearance.SetElement(wearable.ElementType, wearable.ElementIndex);
                }
            }
            // Tout renderer (ré)apparu avec le nouveau look doit rester visible en vue studio.
            if (this._mainCamera != null) this.ApplyPreviewLayer();
        }

        private void RefreshCartUI() {
            int total = 0;
            foreach ((ItemConfig _, int price) in this._cart.Values) total += price;

            int money = LocalMoney();
            if (this.moneyLabel != null) this.moneyLabel.text = $"{money} $";
            if (this.totalLabel != null) this.totalLabel.text = $"Total : {total} $";
            if (this.buyButtonLabel != null) {
                this.buyButtonLabel.text = this._cart.Count > 0 ? $"Acheter ({this._cart.Count})" : "Acheter";
            }
            if (this.buyButton != null) {
                this.buyButton.interactable = !this._pendingPurchase && this._cart.Count > 0 && money >= total;
            }
            this.RebuildCartStrip();
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
            foreach ((ItemConfig cfg, int _) in this._cart.Values) ids[i++] = cfg.ID;

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
            foreach (FittingRoomCartItem item in this._cartItems) {
                if (item != null) Destroy(item.gameObject);
            }
            this._cartItems.Clear();

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

        /// <summary>VUE STUDIO : les renderers du rig du joueur local sont déplacés sur le layer
        /// dédié « Fitting Preview » et la caméra principale ne rend plus QUE ce layer, sur un
        /// fond uni — plus RIEN ne peut obstruer la vue (portants, murs, vitrines, autres
        /// joueurs…). Ne PAS se fier au layer d'origine des meshes : APS2 instancie son rig sur
        /// Default (seule la racine joueur est sur Player). Restauré par
        /// <see cref="RestoreStudioRendering"/> à la fermeture.</summary>
        private void ApplyStudioRendering() {
            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
            if (cam == null) return;

            this._mainCamera = cam;
            this._prevCullingMask = cam.cullingMask;
            this._prevClearFlags = cam.clearFlags;
            this._prevBackground = cam.backgroundColor;

            this._previewLayer = LayerMask.NameToLayer(PreviewLayerName);
            if (this._previewLayer < 0) {
                Debug.LogWarning($"[FittingRoomUI] Layer '{PreviewLayerName}' missing in TagManager — falling back to 'Player'.");
                this._previewLayer = LayerMask.NameToLayer("Player");
            }
            this.ApplyPreviewLayer();

            cam.cullingMask = 1 << this._previewLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = this.studioBackground;
        }

        /// <summary>Pose le layer de preview sur chaque renderer du rig APS2 du joueur local
        /// (idempotent — rejoué après chaque changement de look : les éléments activés par APS2
        /// gardent le layer d'origine du prefab). Les layers d'origine sont mémorisés une fois.</summary>
        private void ApplyPreviewLayer() {
            if (this._previewLayer < 0 || this._player == null) return;

            Transform rigRoot = this._appearance != null && this._appearance.Customization != null
                ? this._appearance.Customization.transform
                : this._player.transform;

            foreach (Renderer renderer in rigRoot.GetComponentsInChildren<Renderer>(true)) {
                GameObject go = renderer.gameObject;
                if (go.layer == this._previewLayer) continue;
                this._previewLayerBackup.Add((go, go.layer));
                go.layer = this._previewLayer;
            }
        }

        private void RestoreStudioRendering() {
            foreach ((GameObject go, int layer) in this._previewLayerBackup) {
                if (go != null) go.layer = layer;
            }
            this._previewLayerBackup.Clear();
            this._previewLayer = -1;

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
