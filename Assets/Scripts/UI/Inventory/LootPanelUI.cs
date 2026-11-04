using System.Collections.Generic;
using Mirror;
using Sim;
using UnityEngine;

/// <summary>
/// Overlay « Récupération » plein écran : grille du conteneur/loot ouvert à GAUCHE,
/// vue inventaire (poche) DUPLIQUÉE à DROITE. Remplace l'ancien panneau flottant du
/// canal Prop (<see cref="ContainerPanelUI"/> reste en service pour le canal Item —
/// colis/sac affiché avec l'inventaire).
///
/// Volontairement AUTONOME : il ne réutilise PAS le panneau inventaire existant
/// (exigence « dupliquer, pas merger ») — il a ses propres slots poche, alimentés par
/// les mêmes snapshots serveur (S2C_PocketSync). Seule l'infrastructure est partagée :
/// pool de DraggableItem (InventoryUI), ItemSlot, messages réseau.
///
/// Le composant vit sur un GameObject ACTIF (les abonnements se font à l'Awake) ;
/// <see cref="root"/> est l'enfant visuel activé/désactivé. UI autorée dans le prefab
/// HUD Manager par Tools ▸ The BROZ ▸ Build Loot Overlay UI.
/// </summary>
public class LootPanelUI : MonoBehaviour, Sim.UI.IHudOverlay
{
    public static LootPanelUI Instance { get; private set; }

    // ── IHudOverlay : plein écran EXCLUSIF (ferme l'inventaire et réciproquement),
    // mais ne masque PAS le HUD de jeu — le monde, les vitals et la hotbar restent
    // visibles derrière l'overlay (maquette). La hotbar reste à sa place d'origine.
    public Sim.UI.OverlayLayer Layer => Sim.UI.OverlayLayer.Fullscreen;
    public bool HidesGameHud => false;
    public bool ShowsHotbar  => false;
    // OnClosed est idempotent côté manager (l'exclusivité nous retire de la pile AVANT
    // d'appeler CloseOverlay) → on notifie toujours, comme InventoryUI.
    public void CloseOverlay() => Close();

    [Header("Root (enfant visuel activé/désactivé — PAS ce GameObject)")]
    [SerializeField] private GameObject root;

    [Header("Panneau conteneur (gauche)")]
    [SerializeField] private TMPro.TMP_Text containerTitle;
    [SerializeField] private TMPro.TMP_Text containerCount;
    [SerializeField, Tooltip("Parent GridLayoutGroup des slots du conteneur.")]
    private Transform slotsContainer;
    [SerializeField, Tooltip("Template de slot conteneur (INACTIF, cloné).")]
    private ItemSlot slotTemplate;
    [SerializeField, Tooltip("LayoutElement de la zone scrollable des slots (hauteur plafonnée à MaxVisibleRows rangées).")]
    private UnityEngine.UI.LayoutElement slotsScrollElement;
    [SerializeField, Tooltip("Tri alphabétique de la grille conteneur (C2S_SortContainer).")]
    private UnityEngine.UI.Button sortContainerButton;
    [SerializeField, Tooltip("Optimisation de la grille conteneur — merge des piles au max (C2S_OptimizeContainer).")]
    private UnityEngine.UI.Button optimizeContainerButton;
    [SerializeField] private UnityEngine.UI.Button takeAllButton;
    [SerializeField, Tooltip("Raccourci « Tout prendre » (position physique de touche).")]
    private UnityEngine.InputSystem.Key takeAllKey = UnityEngine.InputSystem.Key.F;

    [Header("Panneau inventaire dupliqué (droite)")]
    [SerializeField] private TMPro.TMP_Text inventoryCount;
    [SerializeField, Tooltip("Parent GridLayoutGroup des slots poche dupliqués.")]
    private Transform pocketGrid;
    [SerializeField, Tooltip("Template de slot poche (INACTIF, cloné — rejectsNonPocketable).")]
    private ItemSlot pocketSlotTemplate;
    [SerializeField, Tooltip("LayoutElement de la zone scrollable de la poche.")]
    private UnityEngine.UI.LayoutElement pocketScrollElement;
    [SerializeField, Tooltip("Tri alphabétique de la poche.")]
    private UnityEngine.UI.Button sortPocketButton;
    [SerializeField, Tooltip("Optimisation de la poche (fusion des liasses).")]
    private UnityEngine.UI.Button optimizePocketButton;

    [Header("Divers")]
    [SerializeField] private UnityEngine.UI.Button closeButton;
    [SerializeField, Tooltip("Canvas du Root (overrideSorting) : rendu AU-DESSUS de la minimap " +
        "(order 10) mais sous le menu contextuel (1000) et la tooltip (1200).")]
    private Canvas rootCanvas;
    [SerializeField, Tooltip("Minimap du HUD : masquée tant que l'overlay de fouille est ouvert.")]
    private GameObject minimapRoot;
    private bool _minimapWasActive;

    /// <summary>Au-delà de ce nombre de rangées visibles, la grille scrolle.</summary>
    private const int MaxVisibleRows = 3;
    private const int OverlaySortingOrder = 20;

    // ── État conteneur (gauche) ───────────────────────────────────────────────
    private readonly List<ItemSlot> _slots = new List<ItemSlot>();
    private readonly List<DraggableItem> _spawnedItems = new List<DraggableItem>();
    private string _currentPlaceId;
    private int    _currentPropId;
    private string _currentName;
    private bool   _loading;
    private bool   _lootAutoClose;      // loot (cadavre/sac) : se ferme seul une fois vidé
    private CanvasGroup _slotsGroup;

    // ── État poche dupliquée (droite) ────────────────────────────────────────
    private readonly List<ItemSlot> _pocketSlots = new List<ItemSlot>();
    private const int DefaultPocketSlotCount = 8;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (slotTemplate != null)       slotTemplate.gameObject.SetActive(false);
        if (pocketSlotTemplate != null) pocketSlotTemplate.gameObject.SetActive(false);
        if (closeButton != null)        closeButton.onClick.AddListener(() => Close());
        if (takeAllButton != null)      takeAllButton.onClick.AddListener(OnTakeAllClicked);
        if (sortContainerButton != null)     sortContainerButton.onClick.AddListener(() => SendMaintenance(sort: true,  pocketGridTarget: false));
        if (optimizeContainerButton != null) optimizeContainerButton.onClick.AddListener(() => SendMaintenance(sort: false, pocketGridTarget: false));
        if (sortPocketButton != null)        sortPocketButton.onClick.AddListener(() => SendMaintenance(sort: true,  pocketGridTarget: true));
        if (optimizePocketButton != null)    optimizePocketButton.onClick.AddListener(() => SendMaintenance(sort: false, pocketGridTarget: true));

        if (slotsContainer != null) {
            _slotsGroup = slotsContainer.GetComponent<CanvasGroup>()
                       ?? slotsContainer.gameObject.AddComponent<CanvasGroup>();
        }

        // Abonnements permanents (GameObject actif en continu, root seul toggle) —
        // même pattern que ContainerPanelUI. Canal Prop intégralement repris ici.
        ClientItemManager.ContainerOpened         += OnContainerOpened;
        ClientItemManager.ContainerOpenFailed     += OnContainerOpenFailed;
        StorageContainerBehaviour.OnOpenRequested += OnOptimisticOpenRequested;
        ClientItemManager.MoveItemResult          += OnMoveItemResult;
        ClientItemManager.PocketSync              += OnPocketSync;
        DraggableItem.OnDoubleClick += OnItemDoubleClicked;
        DraggableItem.OnStartDrag   += OnItemStartDrag;
        DraggableItem.OnDragEnded   += OnItemDragEnded;
        ItemSlot.OnItemMove         += OnItemMoved;
        ItemSlot.OnItemSwap         += OnItemsSwapped;

        Show(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ClientItemManager.ContainerOpened         -= OnContainerOpened;
        ClientItemManager.ContainerOpenFailed     -= OnContainerOpenFailed;
        StorageContainerBehaviour.OnOpenRequested -= OnOptimisticOpenRequested;
        ClientItemManager.MoveItemResult          -= OnMoveItemResult;
        ClientItemManager.PocketSync              -= OnPocketSync;
        DraggableItem.OnDoubleClick -= OnItemDoubleClicked;
        DraggableItem.OnStartDrag   -= OnItemStartDrag;
        DraggableItem.OnDragEnded   -= OnItemDragEnded;
        ItemSlot.OnItemMove         -= OnItemMoved;
        ItemSlot.OnItemSwap         -= OnItemsSwapped;
    }

    // ── Prédicats publics ─────────────────────────────────────────────────────

    /// <summary>Overlay affiché à l'écran (dès l'ouverture optimiste).</summary>
    public bool IsDisplayed => root != null && root.activeInHierarchy;

    /// <summary>Session prête : snapshot reçu, PlaceId connu → moves routables.</summary>
    public bool IsOpen => !_loading && !string.IsNullOrEmpty(_currentPlaceId);

    /// <summary>UUID backend de la place conteneur en cours.</summary>
    public string PlaceId => _currentPlaceId;

    /// <summary>True tant que le joueur fouille (fenêtre différée comprise) — prédicat du
    /// maintien de l'état BUSY (remplace ContainerPanelUI.IsPropLootOpen pour le loot).</summary>
    public static bool IsLootOpen()
    {
        if (Instance == null) return false;
        if (Instance._deferredWaiting || Instance._hasHeldSnapshot) return true;
        return Instance.IsDisplayed;
    }

    // ── Ouvertures ────────────────────────────────────────────────────────────

    /// <summary>Ouverture optimiste déclenchée par un meuble de rangement
    /// (<see cref="StorageContainerBehaviour.OnOpenRequested"/>).</summary>
    private void OnOptimisticOpenRequested(int propId, int slotCount, string propName)
    {
        _currentPropId = propId;
        _lootAutoClose = false;     // meuble : reste ouvert même vide
        OpenOptimisticCore(slotCount, propName);
    }

    /// <summary>Ouverture optimiste générique (conteneur sans prop : coffre de véhicule…).</summary>
    public void OpenOptimistic(int slotCount, string name)
    {
        _currentPropId = 0;
        _lootAutoClose = true;      // loot : se referme une fois vidé
        OpenOptimisticCore(slotCount, name);
    }

    // ── Fouille différée (cadavre PNJ / sac de butin) ─────────────────────────
    // Le délai est FIXE : la grille s'affiche EXACTEMENT à l'échéance du geste, que le
    // snapshot serveur soit déjà arrivé (grille remplie) ou non (grille en attente,
    // slots non interactifs, remplie à l'arrivée). Le retour serveur ne module jamais
    // la durée du geste — seul le filet de timeout abandonne une fouille sans réponse.
    private const float DefaultSearchOpenDelay = 0.3f;
    private const float SearchOpenTimeout = 5f;

    private float _deferredShowAt = -1f;
    private bool  _deferredWaiting;
    private int   _deferredSlotCount;
    private float _deferredTimeoutAt = -1f;
    private bool  _hasHeldSnapshot;
    private S2C_ContainerOpened _heldSnapshot;
    private bool  _searchGestureOn;

    /// <summary>Fouille différée : rien ne s'affiche avant <paramref name="searchDelay"/>
    /// (durée FIXE du geste de fouille). À l'échéance la grille apparaît — remplie si le
    /// snapshot est là, sinon en attente. L'appelant envoie son C2S de fouille juste après.</summary>
    public void OpenSearchDelayed(int slotCount, string name, float searchDelay = DefaultSearchOpenDelay)
    {
        _currentPropId     = 0;
        _lootAutoClose     = true;
        _currentName       = name;
        _currentPlaceId    = null;
        _loading           = true;
        _deferredShowAt    = Time.time + Mathf.Max(0f, searchDelay);
        _deferredWaiting   = true;
        _deferredSlotCount = slotCount;
        _deferredTimeoutAt = -1f;
        _hasHeldSnapshot   = false;
        SetSearchGesture(true);
    }

    private void Update()
    {
        // Raccourci « Tout prendre ».
        if (IsOpen && takeAllButton != null && takeAllButton.gameObject.activeInHierarchy
            && takeAllButton.interactable && Sim.Utils.GameInput.Down(takeAllKey)
            && !Sim.Utils.CursorLock.IsTypingInInputField()) {
            OnTakeAllClicked();
        }

        if (_deferredWaiting && Time.time >= _deferredShowAt) {
            _deferredWaiting = false;
            if (_hasHeldSnapshot) {
                // Snapshot arrivé pendant le geste : grille directement remplie.
                _hasHeldSnapshot = false;
                OnContainerOpened(_heldSnapshot);
            } else {
                // Serveur plus lent que le geste : la grille s'affiche QUAND MÊME à
                // l'échéance (vide, slots verrouillés) et se remplira à l'arrivée du
                // snapshot. Filet : abandon propre si le serveur ne répond jamais.
                SetSearchGesture(false);
                OpenOptimisticCore(_deferredSlotCount, _currentName);
                _deferredTimeoutAt = Time.time + SearchOpenTimeout;
            }
            return;
        }
        if (_deferredTimeoutAt > 0f && _loading && Time.time > _deferredTimeoutAt) {
            WorldToastManager.ShowError("Fouille impossible");
            Close();
        }
    }

    private void SetSearchGesture(bool on)
    {
        if (on == _searchGestureOn) return;
        _searchGestureOn = on;
        var pc = PlayerController.Local;
        var anim = pc != null ? pc.GetComponent<PlayerAnimator>() : null;
        if (anim != null) anim.SetAction(on ? CharacterAnimatorAction.SEARCHING : CharacterAnimatorAction.NONE);
    }

    private void OpenOptimisticCore(int slotCount, string name)
    {
        _currentName    = name;
        _currentPlaceId = null;     // renseigné par le snapshot
        _loading        = true;

        Show(true);
        EnsureSlotPool(slotCount);
        ConfigureSlots(null, slotCount);
        ApplyScrollHeight(slotsScrollElement, slotsContainer, slotCount);
        ReleaseSpawnedItems();
        RefreshHeaders(0);
        SetSlotsInteractive(false);
    }

    /// <summary>Plafonne la zone scrollable à <see cref="MaxVisibleRows"/> rangées : en deçà
    /// elle épouse la grille (pas de scroll), au-delà la grille défile (molette/scrollbar).</summary>
    private static void ApplyScrollHeight(UnityEngine.UI.LayoutElement el, Transform gridT, int slotCount)
    {
        if (el == null || gridT == null) return;
        var grid = gridT.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid == null) return;
        int cols = grid.constraint == UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount
                 ? Mathf.Max(1, grid.constraintCount) : 4;
        int rows = Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)cols));
        int visible = Mathf.Min(rows, MaxVisibleRows);
        float h = visible * grid.cellSize.y
                + Mathf.Max(0, visible - 1) * grid.spacing.y
                + grid.padding.top + grid.padding.bottom;
        el.minHeight = h;
        el.preferredHeight = h;
    }

    // ── Snapshots serveur ─────────────────────────────────────────────────────

    private void OnContainerOpened(S2C_ContainerOpened msg)
    {
        // Fouille différée : snapshot arrivé avant l'échéance du geste → retenu, la grille
        // ne s'affichera qu'à l'échéance (délai FIXE). Après l'échéance, Update a déjà
        // basculé en affichage optimiste — flux normal ici.
        if (_deferredWaiting) {
            if (Time.time < _deferredShowAt) {
                _heldSnapshot = msg;
                _hasHeldSnapshot = true;
                return;
            }
            _deferredWaiting = false;
        }
        _deferredTimeoutAt = -1f; // snapshot reçu → filet désarmé

        // Ouverture directe d'un autre conteneur pendant qu'un loot était affiché :
        // le flag d'auto-fermeture ne s'applique pas au nouveau contenu.
        if (!_loading && msg.PlaceId != _currentPlaceId) _lootAutoClose = false;

        _currentPropId  = msg.PropId;
        SetSearchGesture(false);
        _currentPlaceId = msg.PlaceId;

        // Show AVANT de spawner (hiérarchie active requise pour l'Awake des draggables).
        Show(true);

        int slotCount = msg.SlotCount;
        EnsureSlotPool(slotCount);
        ConfigureSlots(msg.PlaceId, slotCount);
        ApplyScrollHeight(slotsScrollElement, slotsContainer, slotCount);
        ReleaseSpawnedItems();
        SpawnItemsFromSnapshot(msg.Items);
        RefreshHeaders(msg.Items != null ? msg.Items.Length : 0);

        _loading = false;
        SetSlotsInteractive(true);

        // Loot vidé → fermeture automatique.
        if (_lootAutoClose && (msg.Items == null || msg.Items.Length == 0)) {
            Close();
        }
    }

    private void OnContainerOpenFailed(S2C_ContainerOpenFailed msg)
    {
        if (!_loading || (_currentPropId != msg.PropId && msg.PropId != 0)) return;
        if (!string.IsNullOrEmpty(msg.ErrorMessage)) WorldToastManager.ShowError(msg.ErrorMessage);
        Close();
    }

    private void OnPocketSync(S2C_PocketSync msg)
    {
        if (!IsDisplayed) return;
        ApplyPocketSnapshot(msg);
    }

    // ── Fermeture ─────────────────────────────────────────────────────────────

    public void Close()
    {
        StopTakeAll();
        SetSearchGesture(false);
        _deferredWaiting   = false;
        _hasHeldSnapshot   = false;
        _deferredShowAt    = -1f;
        _deferredTimeoutAt = -1f;
        if (NetworkClient.isConnected && _currentPlaceId != null)
            NetworkClient.Send(new C2S_CloseContainer { ItemContainer = false });
        Show(false);
        ReleaseSpawnedItems();
        ReleasePocketItems();
        _currentPlaceId = null;
        _currentPropId  = 0;
        _currentName    = null;
        _loading        = false;
        _lootAutoClose  = false;
        SetSlotsInteractive(true);
    }

    private void Show(bool visible)
    {
        if (root == null) return;
        bool wasVisible = root.activeSelf;
        root.SetActive(visible);
        if (visible == wasVisible) return;

        var overlays = Sim.UI.HudOverlayManager.Instance;
        if (visible) {
            // Au-dessus de la minimap (canvas override order 10). Ré-asserté à chaque
            // ouverture : Unity peut perdre l'overrideSorting d'un canvas imbriqué
            // désactivé/réactivé.
            if (rootCanvas != null) {
                rootCanvas.overrideSorting = true;
                rootCanvas.sortingOrder = OverlaySortingOrder;
            }
            // Exclusivité plein écran : ferme l'inventaire s'il était ouvert.
            overlays?.OnOpened(this);
            // Minimap masquée pendant la fouille (élément de jeu au-dessus du voile sinon).
            if (minimapRoot != null) {
                _minimapWasActive = minimapRoot.activeSelf;
                minimapRoot.SetActive(false);
            }
            Sim.Audio.AudioManager.Instance.PlayUI(Sim.Audio.SfxId.InventoryOpen);
            // Peuple la poche dupliquée depuis le dernier snapshot connu.
            if (ClientItemManager.LastPocketSnapshot.HasValue)
                ApplyPocketSnapshot(ClientItemManager.LastPocketSnapshot.Value);
            else
                EnsurePocketSlots(DefaultPocketSlotCount);
        } else {
            overlays?.OnClosed(this);
            if (minimapRoot != null && _minimapWasActive) minimapRoot.SetActive(true);
            Sim.Audio.AudioManager.Instance.PlayUI(Sim.Audio.SfxId.InventoryClose);
        }
    }

    // ── Grille conteneur (gauche) ─────────────────────────────────────────────

    private void EnsureSlotPool(int count)
    {
        while (_slots.Count < count) {
            var slot = Instantiate(slotTemplate, slotsContainer);
            slot.gameObject.SetActive(false);
            slot.CanSwap = true;
            _slots.Add(slot);
        }
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;
        for (int i = 0; i < _slots.Count; i++) {
            _slots[i].gameObject.SetActive(i < count);
            if (i < count) {
                ReleaseAllDraggableChildren(_slots[i], inv);
                _slots[i].Clear();
            }
        }
    }

    private void ConfigureSlots(string placeId, int count)
    {
        for (int i = 0; i < count && i < _slots.Count; i++) {
            _slots[i].PlaceId   = placeId;
            _slots[i].SlotIndex = i;
        }
    }

    private void SetSlotsInteractive(bool interactive)
    {
        if (_slotsGroup == null) return;
        _slotsGroup.interactable   = interactive;
        _slotsGroup.blocksRaycasts = interactive;
        _slotsGroup.alpha          = interactive ? 1f : 0.6f;
    }

    private void SpawnItemsFromSnapshot(S2C_ContainerItem[] items)
    {
        if (items == null) return;
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;
        if (inv == null) { Debug.LogWarning("[LootPanelUI] InventoryUI introuvable — pool indisponible."); return; }
        foreach (var entry in items) {
            if (entry.SlotIndex < 0 || entry.SlotIndex >= _slots.Count) continue;
            DraggableItem d = inv.RentDraggable();
            if (entry.PropConfigId > 0) {
                var pcfg = DatabaseManager.GetPropsById(entry.PropConfigId);
                d.SetConfiguration(null);
                d.SetSprite(pcfg != null ? pcfg.Sprite : null);
                d.SetDraggable(false);
                d.SetPackedProp(entry.PropConfigId, entry.PropPresetId);
            } else {
                var cfg = DatabaseManager.GetItemConfigById(entry.ConfigId);
                if (cfg == null) { inv.ReleaseDraggable(d); continue; }
                d.SetConfiguration(cfg);
            }
            d.SetEntityId(entry.EntityId);
            d.SetQuantity(entry.Quantity);
            d.SetLoadedAmmo(entry.LoadedAmmo);
            d.SetCashAmount(entry.CashAmount);
            _slots[entry.SlotIndex].SetItem(d);
            _spawnedItems.Add(d);
        }
    }

    private void ReleaseSpawnedItems()
    {
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;
        foreach (var d in _spawnedItems) {
            if (d == null) continue;
            // Draggable transféré hors de nos slots (drag vers la poche) : plus à nous.
            if (d.ItemSlot != null && !_slots.Contains(d.ItemSlot) && !_pocketSlots.Contains(d.ItemSlot)) continue;
            if (inv != null) inv.ReleaseDraggable(d);
            else { d.transform.SetParent(transform, false); d.gameObject.SetActive(false); }
        }
        _spawnedItems.Clear();
    }

    /// <summary>Premier slot conteneur actif vide, ou -1.</summary>
    public int FindFirstFreeSlot()
    {
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i] != null && _slots[i].gameObject.activeSelf && _slots[i].Item == null) return i;
        return -1;
    }

    /// <summary>Premier slot conteneur avec une pile mergeable pour <paramref name="cfg"/>.</summary>
    public int FindMergeableSlotFor(ItemConfig cfg)
    {
        if (cfg == null || !cfg.IsStackable) return -1;
        for (int i = 0; i < _slots.Count; i++) {
            var slot = _slots[i];
            if (slot == null || !slot.gameObject.activeSelf) continue;
            var d = slot.Item;
            if (d == null || d.ItemConfig != cfg) continue;
            if (d.Quantity < cfg.MaxStackSize) return i;
        }
        return -1;
    }

    private static void ReleaseAllDraggableChildren(ItemSlot slot, InventoryUI inv)
    {
        if (slot == null || inv == null) return;
        var t = slot.transform;
        for (int c = t.childCount - 1; c >= 0; c--) {
            var d = t.GetChild(c).GetComponent<DraggableItem>();
            if (d != null) inv.ReleaseDraggable(d);
        }
    }

    // ── Poche dupliquée (droite) ──────────────────────────────────────────────

    private void EnsurePocketSlots(int count)
    {
        if (pocketSlotTemplate == null || pocketGrid == null) return;
        while (_pocketSlots.Count < count) {
            ItemSlot slot = Instantiate(pocketSlotTemplate, pocketGrid);
            slot.name      = $"Loot Pocket Slot {_pocketSlots.Count}";
            slot.SlotIndex = _pocketSlots.Count;
            slot.CanSwap   = true;
            slot.gameObject.SetActive(true);
            _pocketSlots.Add(slot);
        }
        for (int i = 0; i < _pocketSlots.Count; i++)
            if (_pocketSlots[i]) _pocketSlots[i].gameObject.SetActive(i < count);

        string charId = PlayerController.Local != null ? PlayerController.Local.CharacterData?.Id : null;
        if (!string.IsNullOrEmpty(charId))
            foreach (ItemSlot s in _pocketSlots) if (s) s.PlaceId = $"pocket:{charId}";

        ApplyScrollHeight(pocketScrollElement, pocketGrid, count);
    }

    private void ApplyPocketSnapshot(S2C_PocketSync msg)
    {
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;
        if (inv == null) return;

        EnsurePocketSlots(msg.SlotCount > 0 ? msg.SlotCount : DefaultPocketSlotCount);
        ReleasePocketItems();

        int used = 0;
        if (msg.Items != null) {
            foreach (var entry in msg.Items) {
                ItemSlot target = entry.SlotIndex >= 0 && entry.SlotIndex < _pocketSlots.Count
                                ? _pocketSlots[entry.SlotIndex] : null;
                if (target == null) continue;
                var cfg = DatabaseManager.GetItemConfigById(entry.ConfigId);
                if (cfg == null) continue;
                DraggableItem d = inv.RentDraggable();
                d.SetConfiguration(cfg);
                d.SetEntityId(entry.EntityId);
                d.SetQuantity(Mathf.Max(1, entry.Quantity));
                d.SetLoadedAmmo(entry.LoadedAmmo);
                d.SetCashAmount(entry.CashAmount);
                target.SetItem(d);
                used++;
            }
        }
        if (inventoryCount != null) {
            int total = msg.SlotCount > 0 ? msg.SlotCount : DefaultPocketSlotCount;
            inventoryCount.text = $"{used} / {total}";
        }
    }

    private void ReleasePocketItems()
    {
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;
        foreach (ItemSlot s in _pocketSlots) {
            if (s == null) continue;
            var t = s.transform;
            for (int c = t.childCount - 1; c >= 0; c--) {
                var d = t.GetChild(c).GetComponent<DraggableItem>();
                if (d != null && inv != null) inv.ReleaseDraggable(d);
            }
            s.Clear();
        }
    }

    // ── Entêtes ───────────────────────────────────────────────────────────────

    private void RefreshHeaders(int itemCount)
    {
        if (containerTitle != null)
            containerTitle.text = string.IsNullOrEmpty(_currentName) ? "Conteneur" : _currentName;
        if (containerCount != null)
            containerCount.text = itemCount > 0 ? $"{itemCount} objet{(itemCount > 1 ? "s" : "")}" : "";
    }

    // ── Interactions ──────────────────────────────────────────────────────────

    private bool OwnsSlot(ItemSlot slot)
        => slot != null && (_slots.Contains(slot) || _pocketSlots.Contains(slot));

    /// <summary>Double-clic = transfert rapide : conteneur → poche/main, poche → conteneur.</summary>
    private void OnItemDoubleClicked(DraggableItem item)
    {
        if (!IsOpen || item == null || item.ItemSlot == null) return;
        if (item.IsPackedProp) return; // meuble emballé : déballage build-mode uniquement

        if (_slots.Contains(item.ItemSlot)) {
            var inv = HUDManager.Instance?.InventoryUI;
            if (inv == null) return;
            if (!inv.TryFindQuickMoveTargetForItem(item, out string placeKey, out int slotIndex)) {
                WorldToastManager.ShowError(InventoryToasts.NoSpaceInInventory);
                return;
            }
            SendMove(item.EntityId, item.ItemSlot.PlaceId, placeKey, slotIndex);
        }
        else if (_pocketSlots.Contains(item.ItemSlot)) {
            int target = FindMergeableSlotFor(item.ItemConfig);
            if (target < 0) target = FindFirstFreeSlot();
            if (target < 0) { WorldToastManager.ShowError(InventoryToasts.ContainerFull); return; }
            if (string.IsNullOrEmpty(_currentPlaceId)) return;
            SendMove(item.EntityId, item.ItemSlot.PlaceId, _currentPlaceId, target);
        }
    }

    /// <summary>Tri alphabétique / optimisation d'une des deux grilles (serveur-autoritaire ;
    /// le snapshot re-poussé rafraîchit l'UI — succès silencieux).</summary>
    private void SendMaintenance(bool sort, bool pocketGridTarget)
    {
        if (!NetworkClient.isConnected) return;
        string placeId;
        if (pocketGridTarget) {
            string charId = PlayerController.Local != null ? PlayerController.Local.CharacterData?.Id : null;
            if (string.IsNullOrEmpty(charId)) return;
            placeId = $"pocket:{charId}";
        } else {
            if (!IsOpen) return; // grille conteneur pas prête (PlaceId inconnu)
            placeId = _currentPlaceId;
        }
        if (sort) NetworkClient.Send(new C2S_SortContainer     { PlaceId = placeId });
        else      NetworkClient.Send(new C2S_OptimizeContainer { PlaceId = placeId });
    }

    private static void SendMove(int entityId, string fromPlace, string toPlace, int toSlot)
    {
        if (!NetworkClient.isConnected || string.IsNullOrEmpty(fromPlace) || string.IsNullOrEmpty(toPlace)) return;
        NetworkClient.Send(new C2S_MoveItem {
            EntityId    = entityId,
            FromPlaceId = fromPlace,
            ToPlaceId   = toPlace,
            ToSlotIndex = toSlot,
        });
    }

    /// <summary>Pendant un drag : grise les slots poche inéligibles (non-pocketable).</summary>
    private void OnItemStartDrag(DraggableItem dragged)
    {
        if (!IsDisplayed || dragged == null || dragged.ItemConfig == null) return;
        bool allowedInPocket = dragged.ItemConfig.AllowedInPocket;
        foreach (ItemSlot s in _pocketSlots) if (s) s.SetDropAcceptance(allowedInPocket);
        if (dragged.ItemSlot != null) dragged.ItemSlot.SetDropAcceptance(true);
    }

    private void OnItemDragEnded(DraggableItem dragged)
    {
        if (!IsDisplayed) return;
        foreach (ItemSlot s in _pocketSlots) if (s) s.SetDropAcceptance(true);
    }

    // ── Routage des moves/swaps (dupliqué d'InventoryUI — l'inventaire est FERMÉ
    // pendant l'overlay, son routeur n'écoute pas) ────────────────────────────

    private void OnItemMoved(ItemSlot originSlot, ItemSlot targetSlot)
    {
        if (!IsDisplayed) return;
        if (!OwnsSlot(originSlot) && !OwnsSlot(targetSlot)) return;

        string fromPlace = originSlot.PlaceId;
        string toPlace   = targetSlot.PlaceId;
        if (string.IsNullOrEmpty(fromPlace) || string.IsNullOrEmpty(toPlace)) return;
        if (fromPlace == toPlace && originSlot.SlotIndex == targetSlot.SlotIndex) return;

        var placeholder = targetSlot.Item;
        int entityId = placeholder != null ? placeholder.EntityId : 0;
        if (entityId <= 0) return;
        if (!NetworkClient.isConnected) return;

        NetworkClient.Send(new C2S_MoveItem {
            EntityId    = entityId,
            FromPlaceId = fromPlace,
            ToPlaceId   = toPlace,
            ToSlotIndex = targetSlot.SlotIndex,
        });

        // Placeholder : le snapshot serveur repopule — release différé (laisse finir le tween).
        targetSlot.Clear();
        if (placeholder != null && placeholder.LandDuration > 0f) {
            StartCoroutine(DeferredReleaseCoroutine(placeholder, placeholder.LandDuration + 0.02f));
        } else {
            HUDManager.Instance?.InventoryUI?.ReleaseDraggable(placeholder);
        }
    }

    private System.Collections.IEnumerator DeferredReleaseCoroutine(DraggableItem d, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (d == null) yield break;
        if (d.ItemSlot != null && d.ItemSlot.Item == d) yield break; // recyclé par un snapshot
        HUDManager.Instance?.InventoryUI?.ReleaseDraggable(d);
    }

    private void OnItemsSwapped(ItemSlot slotA, DraggableItem itemA, ItemSlot slotB, DraggableItem itemB)
    {
        if (!IsDisplayed) return;
        if (!OwnsSlot(slotA) && !OwnsSlot(slotB)) return;
        if (string.IsNullOrEmpty(slotA.PlaceId) || string.IsNullOrEmpty(slotB.PlaceId)) return;
        if (slotA.PlaceId == slotB.PlaceId && slotA.SlotIndex == slotB.SlotIndex) return;
        int entityA = itemA != null ? itemA.EntityId : 0;
        int entityB = itemB != null ? itemB.EntityId : 0;
        if (entityA <= 0 || entityB <= 0 || !NetworkClient.isConnected) return;

        NetworkClient.Send(new C2S_SwapItems {
            EntityIdA  = entityA,
            PlaceIdA   = slotA.PlaceId,
            SlotIndexA = slotA.SlotIndex,
            EntityIdB  = entityB,
            PlaceIdB   = slotB.PlaceId,
            SlotIndexB = slotB.SlotIndex,
        });
    }

    private void OnMoveItemResult(S2C_MoveItemResult msg)
    {
        if (_takeAllRoutine != null && !msg.Success) {
            _takeAllMoveFailed = true;
            _takeAllMoveEntity = msg.EntityId;
        }
        if (!IsDisplayed || msg.Success) return;
        if (!string.IsNullOrEmpty(msg.ErrorMessage)) WorldToastManager.ShowError(msg.ErrorMessage);
        // Les snapshots serveur (container + pocket) re-poussés après un refus reconcilient.
    }

    // ── « Tout prendre » — repris de ContainerPanelUI (cibles poche/main) ────────

    private Coroutine _takeAllRoutine;
    private bool _takeAllMoveFailed;
    private int  _takeAllMoveEntity;
    private const float TakeAllStepTimeout = 2.5f;

    private void OnTakeAllClicked()
    {
        if (!IsOpen || _takeAllRoutine != null) return;
        _takeAllRoutine = StartCoroutine(TakeAllCoroutine());
    }

    private void StopTakeAll()
    {
        if (_takeAllRoutine != null) { StopCoroutine(_takeAllRoutine); _takeAllRoutine = null; }
        if (takeAllButton != null) takeAllButton.interactable = true;
    }

    private System.Collections.IEnumerator TakeAllCoroutine()
    {
        if (takeAllButton != null) takeAllButton.interactable = false;
        var skipped = new HashSet<int>();
        var inv = HUDManager.Instance?.InventoryUI;

        while (IsDisplayed && inv != null) {
            DraggableItem pick = null;
            foreach (var slot in _slots) {
                if (slot == null || !slot.gameObject.activeSelf) continue;
                var d = slot.Item;
                if (d == null || d.IsPackedProp || d.EntityId <= 0) continue;
                if (skipped.Contains(d.EntityId)) continue;
                pick = d;
                break;
            }
            if (pick == null) break;

            if (!inv.TryFindQuickMoveTargetForItem(pick, out string toPlace, out int toSlot)) {
                skipped.Add(pick.EntityId);
                continue;
            }

            int eid = pick.EntityId;
            int qtyBefore = pick.Quantity;
            _takeAllMoveFailed = false;
            _takeAllMoveEntity = 0;
            if (!NetworkClient.isConnected) break;
            SendMove(eid, _currentPlaceId, toPlace, toSlot);

            float deadline = Time.time + TakeAllStepTimeout;
            while (Time.time < deadline) {
                if (_takeAllMoveFailed && _takeAllMoveEntity == eid) break;
                if (!GridStillHas(eid, qtyBefore)) break;
                yield return null;
            }
            if ((_takeAllMoveFailed && _takeAllMoveEntity == eid) || GridStillHas(eid, qtyBefore)) {
                skipped.Add(eid);
            }
        }

        bool remaining = false;
        foreach (var slot in _slots) {
            var d = slot != null && slot.gameObject.activeSelf ? slot.Item : null;
            if (d != null && !d.IsPackedProp) { remaining = true; break; }
        }
        if (remaining && IsDisplayed) WorldToastManager.ShowError(InventoryToasts.NoSpaceInInventory);

        _takeAllRoutine = null;
        if (takeAllButton != null) takeAllButton.interactable = true;
    }

    private bool GridStillHas(int entityId, int quantity)
    {
        foreach (var slot in _slots) {
            if (slot == null || !slot.gameObject.activeSelf) continue;
            var d = slot.Item;
            if (d != null && d.EntityId == entityId) return d.Quantity == quantity;
        }
        return false;
    }
}
