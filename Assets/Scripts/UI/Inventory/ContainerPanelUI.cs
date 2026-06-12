using System.Collections.Generic;
using Mirror;
using Sim;
using UnityEngine;

/// <summary>
/// Panneau de stockage : reçoit <see cref="S2C_ContainerOpened"/>, génère N slots
/// dans une grille à partir d'un template, peuple les items existants. Drag&drop
/// vers/depuis ces slots passe par l'event <c>ItemSlot.OnItemMove</c> que
/// <see cref="InventoryUI"/> route en <see cref="C2S_MoveItem"/>.
///
/// Pool dédié de DraggableItem pour ne pas interférer avec celui de l'inventaire perso.
/// Le GameObject porteur doit rester actif ; <see cref="root"/> est un enfant
/// activé/désactivé.
/// </summary>
/// <summary>Canal d'un <see cref="ContainerPanelUI"/> : un panneau dédié aux meubles
/// (Prop) et un dédié au colis tenu (Item) coexistent → drag&drop entre les deux.</summary>
public enum ContainerChannel { Prop, Item }

public class ContainerPanelUI : MonoBehaviour
{
    // Deux instances coexistent (une par canal). Accès statique par canal.
    public static ContainerPanelUI Prop { get; private set; }
    public static ContainerPanelUI Item { get; private set; }

    /// <summary>Panneau cible d'un quick-move depuis l'inventaire : le meuble ouvert en
    /// priorité (focus actif), sinon le colis.</summary>
    public static ContainerPanelUI QuickMoveTarget()
    {
        if (Prop != null && Prop.IsOpen) return Prop;
        if (Item != null && Item.IsOpen) return Item;
        return null;
    }

    [Header("Canal (Prop = meuble, Item = colis tenu)")]
    [SerializeField] private ContainerChannel channel = ContainerChannel.Prop;
    private bool IsItemChannel => channel == ContainerChannel.Item;

    [Header("Root (enfant à activer/désactiver — PAS ce GameObject)")]
    [SerializeField] private GameObject root;

    [Header("Grid")]
    [Tooltip("Parent où sont instanciés les ItemSlot (idéalement avec un GridLayoutGroup).")]
    [SerializeField] private Transform slotsContainer;
    [Tooltip("Template de slot vide (gardé INACTIF, cloné une fois par slot).")]
    [SerializeField] private ItemSlot slotTemplate;

    // Pool partagé avec InventoryUI (HUDManager.Instance.InventoryUI). Permet de
    // réutiliser les mêmes DraggableItem entre mains/poches/conteneur sans flicker
    // lors d'un drop cross-place (le draggable est juste reparenté de slot à slot).

    [Header("Optional UI")]
    [SerializeField] private UnityEngine.UI.Button closeButton;
    [SerializeField] private TMPro.TMP_Text titleText;

    [Header("Dynamic sizing")]
    [Tooltip("RectTransform du panneau visible (typiquement le 'Root') redimensionné en fonction du nombre de slots.")]
    [SerializeField] private RectTransform panelRect;
    [Tooltip("LayoutElement du conteneur de slots (ex: 'Slots Scroll View') ajusté à la hauteur de la grille.")]
    [SerializeField] private UnityEngine.UI.LayoutElement slotsLayoutElement;
    [Tooltip("Hauteur réservée aux éléments hors grille (titre + bouton de fermeture + paddings verticaux du VerticalLayoutGroup).")]
    [SerializeField] private float verticalChromeHeight = 140f;

    private readonly List<ItemSlot> _slots = new List<ItemSlot>();
    private readonly List<DraggableItem> _spawnedItems = new List<DraggableItem>();

    private string _currentPlaceId;
    private int    _currentPropId;
    private int    _currentItemEntityId = -1; // package en cours (item-container)
    private bool   _isItemContainer;          // source de l'ouverture courante
    private string _currentPropName;     // cache pour le titre, valorisé à l'ouverture optimiste
    private bool   _subscribed;
    private bool   _loading;
    private bool   _heldDriven;          // l'item-conteneur affiché est piloté par « tenu en main »
    private int    _autoOpenedEntityId = -1; // colis pour lequel l'auto-open a déjà été déclenché
    private int    _openRetries;         // tentatives d'auto-open en attendant l'UUID (spawn async)
    private const int MaxOpenRetries = 8;

    // Protos d'actions du menu contextuel (clic droit dans la grille). Clonées par
    // InventoryActionMenu.ShowSingleAction.
    private Sim.Interactables.Action _protoPoser;
    private Sim.Interactables.Action _protoDrop;
    private UnityEngine.CanvasGroup _slotsGroup;

    private void Awake()
    {
        if (IsItemChannel) {
            if (Item != null && Item != this) { Destroy(gameObject); return; }
            Item = this;
        } else {
            if (Prop != null && Prop != this) { Destroy(gameObject); return; }
            Prop = this;
        }
        _isItemContainer = IsItemChannel; // constant par panneau

        if (slotTemplate != null) slotTemplate.gameObject.SetActive(false);
        if (closeButton  != null) closeButton.onClick.AddListener(Close);

        // CanvasGroup ciblé sur la GRILLE seulement (pas la racine) : on bloque les
        // drops sur les slots pendant l'attente du snapshot, sans empêcher le close
        // button d'être cliquable si le serveur traîne.
        if (slotsContainer != null) {
            _slotsGroup = slotsContainer.GetComponent<UnityEngine.CanvasGroup>()
                       ?? slotsContainer.gameObject.AddComponent<UnityEngine.CanvasGroup>();
        }

        Subscribe();
        Show(false);
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (Item == this) Item = null;
        if (Prop == this) Prop = null;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        // Chaque panneau n'écoute QUE les events de son canal → les deux coexistent.
        if (IsItemChannel) {
            ClientItemManager.ItemContainerOpened     += OnItemContainerOpened;
            ClientItemManager.ItemContainerOpenFailed += OnItemContainerOpenFailed;
            PackageItemBehaviour.OnItemContainerOpenRequested += OnItemContainerOpenRequested;
            PlayerHands.OnHandChanged                 += OnLocalHandsChanged;
            DraggableItem.OnRightClick                += OnEntryRightClicked;
        } else {
            ClientItemManager.ContainerOpened     += OnContainerOpened;
            ClientItemManager.ContainerOpenFailed += OnContainerOpenFailed;
            StorageContainerBehaviour.OnOpenRequested += OnOptimisticOpenRequested;
        }
        DraggableItem.OnDoubleClick           += OnItemDoubleClicked;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (IsItemChannel) {
            ClientItemManager.ItemContainerOpened     -= OnItemContainerOpened;
            ClientItemManager.ItemContainerOpenFailed -= OnItemContainerOpenFailed;
            PackageItemBehaviour.OnItemContainerOpenRequested -= OnItemContainerOpenRequested;
            PlayerHands.OnHandChanged                 -= OnLocalHandsChanged;
            DraggableItem.OnRightClick                -= OnEntryRightClicked;
        } else {
            ClientItemManager.ContainerOpened     -= OnContainerOpened;
            ClientItemManager.ContainerOpenFailed -= OnContainerOpenFailed;
            StorageContainerBehaviour.OnOpenRequested -= OnOptimisticOpenRequested;
        }
        DraggableItem.OnDoubleClick           -= OnItemDoubleClicked;
        _subscribed = false;
    }

    /// <summary>True quand une session conteneur est ouverte ET prête (snapshot reçu).
    /// Tant que <see cref="_loading"/> est vrai, le PlaceId backend n'est pas connu et les
    /// quick-moves doivent attendre.</summary>
    public bool IsOpen => !_loading && !string.IsNullOrEmpty(_currentPlaceId);

    /// <summary>UUID backend de la place conteneur en cours. Utilisé pour router un quick-move.</summary>
    public string PlaceId => _currentPlaceId;

    /// <summary>True quand le conteneur ouvert est un item-conteneur (« package »), prêt.</summary>
    public bool IsItemContainerOpen => IsOpen && _isItemContainer;

    /// <summary>Premier slot actif vide, ou -1 si plein.</summary>
    public int FindFirstFreeSlot()
    {
        for (int i = 0; i < _slots.Count; i++) {
            var slot = _slots[i];
            if (slot != null && slot.gameObject.activeSelf && slot.Item == null) return i;
        }
        return -1;
    }

    /// <summary>Nombre de slots actifs vides (espace restant).</summary>
    public int FreeSlotCount()
    {
        int n = 0;
        for (int i = 0; i < _slots.Count; i++) {
            var slot = _slots[i];
            if (slot != null && slot.gameObject.activeSelf && slot.Item == null) n++;
        }
        return n;
    }

    /// <summary>True si ce panneau affiche actuellement l'item-conteneur dont le colis a
    /// l'entityId donné (utilisé par la tooltip pour montrer l'espace restant réel).</summary>
    public bool IsShowingItemContainer(int packageEntityId) =>
        IsItemContainerOpen && _currentItemEntityId == packageEntityId;

    /// <summary>True si <paramref name="slot"/> appartient à la grille de ce conteneur.</summary>
    public bool OwnsSlot(ItemSlot slot) => slot != null && _slots.Contains(slot);

    /// <summary>
    /// Clic droit sur une entrée de la grille → menu contextuel HUD :
    ///  - meuble emballé → « Poser » (mode placement, le serveur déplace le meuble, UUID conservé) ;
    ///  - item normal, SI le colis ouvert est tenu en main → « Lâcher » (au sol).
    /// </summary>
    private void OnEntryRightClicked(DraggableItem item)
    {
        if (!IsItemChannel || !IsOpen || item == null || item.ItemSlot == null) return;
        if (!OwnsSlot(item.ItemSlot)) return;

        var menu = InventoryActionMenu.Shared;
        if (menu == null) return;

        if (item.IsPackedProp) {
            if (_protoPoser == null) _protoPoser = Resources.Load<Sim.Interactables.Action>("Configurations/Actions/POSER");
            if (_protoPoser == null) return;
            int cfgId = item.PropConfigId, preset = item.PropPresetId, pkg = _currentItemEntityId, slot = item.ItemSlot.SlotIndex;
            menu.ShowSingleAction(_protoPoser, () => StartUnpack(cfgId, preset, pkg, slot));
        } else {
            // « Lâcher » uniquement si le colis ouvert est actuellement tenu en main.
            if (!IsLocalHeldEntity(_currentItemEntityId)) return;
            if (_protoDrop == null) _protoDrop = Resources.Load<Sim.Interactables.Action>("Configurations/Actions/DROP");
            if (_protoDrop == null) return;
            int eid = item.EntityId;
            menu.ShowSingleAction(_protoDrop, () => {
                if (NetworkClient.isConnected) NetworkClient.Send(new C2S_DropFromInventory { EntityId = eid });
            });
        }
    }

    private void StartUnpack(int propConfigId, int presetId, int packageEntityId, int slotIndex)
    {
        PlayerController pc = PlayerController.Local;
        if (pc == null) return;
        ApartmentController apt = pc.CurrentGeographicArea != null
            ? pc.CurrentGeographicArea.GetComponentInParent<ApartmentController>() : null;
        if (apt == null) { WorldToastManager.ShowError("Allez dans un appartement pour déballer"); return; }

        var config = DatabaseManager.GetPropsById(propConfigId);
        var inter  = pc.GetComponent<PlayerInteraction>();
        if (config == null || inter == null) return;

        inter.StartPropUnpack(config, presetId, packageEntityId, slotIndex);
        // Le mode placement prend la main : on referme l'inventaire/grille.
        if (HUDManager.Instance != null) HUDManager.Instance.CloseInventory();
    }

    private void OnItemDoubleClicked(DraggableItem item)
    {
        if (!IsOpen || item == null || item.ItemSlot == null) return;
        if (!OwnsSlot(item.ItemSlot)) return;
        if (!item.IsDraggable) return; // meuble emballé : pas de quick-move (déballage = futur)

        var inv = HUDManager.Instance?.InventoryUI;
        if (inv == null) return;
        if (!inv.TryFindQuickMoveTargetForItem(item, out string toPlace, out int toSlot)) {
            WorldToastManager.Show(InventoryToasts.NoSpaceInInventory);
            return;
        }
        if (!Mirror.NetworkClient.isConnected) return;

        Mirror.NetworkClient.Send(new C2S_MoveItem {
            EntityId    = item.EntityId,
            FromPlaceId = item.ItemSlot.PlaceId,
            ToPlaceId   = toPlace,
            ToSlotIndex = toSlot,
        });
    }

    /// <summary>
    /// Ouverture optimiste : le panneau s'affiche immédiatement avec la bonne taille
    /// de grille, mais les slots bloquent les drops via le CanvasGroup jusqu'à l'arrivée
    /// du snapshot (qui apporte le PlaceId nécessaire au routage C2S_MoveItem).
    /// </summary>
    private void OnOptimisticOpenRequested(int propId, int slotCount, string propName)
    {
        _isItemContainer = false;
        _currentPropId   = propId;
        OpenOptimisticCore(slotCount, propName);
    }

    /// <summary>Ouverture optimiste d'un item-conteneur (« package »).
    /// Déclenchée par l'action OPEN sur un colis AU SOL (non tenu) → affichage non piloté
    /// par la main (ne se referme pas sur changement de main).</summary>
    private void OnItemContainerOpenRequested(int entityId, int slotCount, string displayName)
    {
        _heldDriven          = false;
        _isItemContainer     = true;
        _currentItemEntityId = entityId;
        OpenOptimisticCore(slotCount, displayName);
    }

    /// <summary>
    /// Affichage automatique de la grille dès qu'un colis est tenu en main (et fermeture
    /// au lâcher). Piloté par <see cref="PlayerHands.OnHandChanged"/> du joueur local.
    /// L'ouverture via l'action OPEN d'un colis au sol n'est PAS affectée (_heldDriven=false).
    /// </summary>
    private void OnLocalHandsChanged()
    {
        ItemBehaviour held = LocalHeldContainerItem();
        if (held != null && held.Identity != null)
        {
            int eid = held.Identity.EntityId;
            _heldDriven = true; // un colis tenu pilote l'affichage → se referme au lâcher
            // N'auto-affiche le colis QUE si l'inventaire est déjà ouvert : tenir un colis
            // ne doit PAS forcer l'ouverture du HUD (ex. au lancement du jeu). Quand le
            // joueur ouvrira l'inventaire, RefreshHeldContainer ré-évaluera et l'affichera.
            if (IsInventoryOpen() && eid != _autoOpenedEntityId)
            {
                _autoOpenedEntityId = eid;
                _openRetries        = 0;
                AutoOpenHeldContainer(held);
            }
        }
        else
        {
            _autoOpenedEntityId = -1;
            if (_isItemContainer && _heldDriven && (IsOpen || _loading)) Close();
        }
    }

    private static ItemBehaviour LocalHeldContainerItem()
    {
        PlayerController pc = PlayerController.Local;
        return pc != null && pc.PlayerHands != null ? pc.PlayerHands.HeldContainerItem : null;
    }

    private static bool IsInventoryOpen()
    {
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;
        return inv != null && inv.gameObject.activeSelf;
    }

    private void AutoOpenHeldContainer(ItemBehaviour colis)
    {
        var container = colis.Configuration != null ? colis.Configuration.Container : null;
        int slotCount = container != null ? container.SlotCount : 0;
        if (slotCount <= 0) return;

        _isItemContainer     = true;
        _currentItemEntityId = colis.Identity.EntityId;
        // forceInventory=false : l'inventaire est déjà ouvert (gardé par OnLocalHandsChanged),
        // on ne le force pas — sinon tenir un colis ré-ouvrirait le HUD tout seul.
        OpenOptimisticCore(slotCount, colis.Configuration.Label, forceInventory: false);
        if (NetworkClient.isConnected)
            NetworkClient.Send(new C2S_OpenItemContainer { EntityId = colis.Identity.EntityId });
    }

    private void OpenOptimisticCore(int slotCount, string name, bool forceInventory = true)
    {
        _currentPropName = name;
        _currentPlaceId  = null;    // sera renseigné par le snapshot
        _loading         = true;

        Show(true);
        if (forceInventory && HUDManager.Instance != null) HUDManager.Instance.ShowInventory();

        EnsureSlotPool(slotCount);
        ConfigureSlots(null, slotCount);
        ApplyDynamicHeight(slotCount);
        ReleaseSpawnedItems();
        if (titleText != null) titleText.text = ResolveTitle();

        SetSlotsInteractive(false);
    }

    /// <summary>Titre courant du panneau : nom du prop si connu, sinon fallback générique.</summary>
    private string ResolveTitle() =>
        !string.IsNullOrEmpty(_currentPropName) ? _currentPropName : "Conteneur";

    private void SetSlotsInteractive(bool interactive)
    {
        if (_slotsGroup == null) return;
        _slotsGroup.interactable   = interactive;
        _slotsGroup.blocksRaycasts = interactive;
        _slotsGroup.alpha          = interactive ? 1f : 0.6f;
    }

    private void OnContainerOpened(S2C_ContainerOpened msg)
    {
        _isItemContainer = false;
        _currentPropId   = msg.PropId;
        ApplySnapshotCore(msg.PlaceId, msg.SlotCount, msg.Items);
    }

    private void OnItemContainerOpened(S2C_ItemContainerOpened msg)
    {
        _openRetries         = 0;
        _isItemContainer     = true;
        _currentItemEntityId = msg.EntityId;
        ApplySnapshotCore(msg.PlaceId, msg.SlotCount, msg.Items);
    }

    private void ApplySnapshotCore(string placeId, int slotCount, S2C_ContainerItem[] items)
    {
        // Course rare : l'inventaire a été fermé entre l'auto-open d'un colis tenu et
        // l'arrivée du snapshot. On n'ouvre PAS le HUD tout seul → on abandonne ; le colis
        // se ré-affichera à la prochaine ouverture d'inventaire (RefreshHeldContainer).
        if (_isItemContainer && _heldDriven && !IsInventoryOpen()) { Close(); return; }

        _currentPlaceId = placeId;

        // Show AVANT de spawner : sinon les nouveaux DraggableItem sont instanciés
        // dans une hiérarchie inactive et leur Awake ne tourne pas → _image null
        // → NRE dès SetConfiguration. Show(true) active root + slots.
        // Idempotent quand on vient d'OpenOptimistic (déjà Show(true) + ShowInventory).
        Show(true);
        if (HUDManager.Instance != null) HUDManager.Instance.ShowInventory();

        EnsureSlotPool(slotCount);
        ConfigureSlots(placeId, slotCount);
        ApplyDynamicHeight(slotCount);
        ReleaseSpawnedItems();
        if (titleText != null) titleText.text = ResolveTitle();
        SpawnItemsFromSnapshot(items);

        // Snapshot reçu → panneau pleinement interactif.
        _loading = false;
        SetSlotsInteractive(true);
    }

    private void OnContainerOpenFailed(S2C_ContainerOpenFailed msg)
    {
        Debug.LogWarning($"[ContainerPanelUI] Ouverture refusée propId={msg.PropId} : {msg.ErrorMessage}");
        // Si le panneau était ouvert optimistement pour ce prop, on referme + toast.
        if (_loading && !_isItemContainer && _currentPropId == msg.PropId) {
            if (!string.IsNullOrEmpty(msg.ErrorMessage)) WorldToastManager.Show(msg.ErrorMessage);
            Close();
        }
    }

    private void OnItemContainerOpenFailed(S2C_ItemContainerOpenFailed msg)
    {
        if (!(_loading && _isItemContainer && _currentItemEntityId == msg.EntityId)) return;

        // Course au spawn : un colis fraîchement créé n'a pas encore son UUID DB (l'upsert
        // est async). L'auto-open du « tenu en main » arrive avant. Tant que le colis reste
        // tenu, on retente silencieusement jusqu'à ce que le bridge soit prêt, au lieu de
        // fermer + warning (le retry réussit en général à la 1re ou 2e tentative).
        bool transientUuid = !string.IsNullOrEmpty(msg.ErrorMessage) && msg.ErrorMessage.Contains("UUID");
        if (_heldDriven && transientUuid && IsLocalHeldEntity(msg.EntityId) && _openRetries < MaxOpenRetries) {
            _openRetries++;
            if (isActiveAndEnabled) StartCoroutine(RetryAutoOpenAfter(0.25f, msg.EntityId));
            return;
        }

        Debug.LogWarning($"[ContainerPanelUI] Ouverture package refusée entityId={msg.EntityId} : {msg.ErrorMessage}");
        _openRetries = 0;
        if (!string.IsNullOrEmpty(msg.ErrorMessage)) WorldToastManager.Show(msg.ErrorMessage);
        Close();
    }

    private static bool IsLocalHeldEntity(int entityId)
    {
        ItemBehaviour held = LocalHeldContainerItem();
        return held != null && held.Identity != null && held.Identity.EntityId == entityId;
    }

    private System.Collections.IEnumerator RetryAutoOpenAfter(float delay, int entityId)
    {
        yield return new WaitForSeconds(delay);
        // Toujours le même colis tenu ? Sinon on laisse tomber (OnLocalHandsChanged gère le reste).
        if (!IsLocalHeldEntity(entityId)) { _openRetries = 0; yield break; }
        if (NetworkClient.isConnected)
            NetworkClient.Send(new C2S_OpenItemContainer { EntityId = entityId });
    }

    /// <summary>
    /// Garantit qu'il y a <paramref name="count"/> slots clonés depuis le template.
    /// Les slots existants en surplus sont désactivés.
    /// Pour chaque slot actif : libère TOUS les enfants <see cref="DraggableItem"/>
    /// (et pas juste <c>slot._item</c>). Cela balaie les orphelins issus de swaps
    /// hand↔container où le draggable d'origine n'était pas dans <c>_spawnedItems</c> :
    /// sans ce nettoyage, ils s'accumulent dans les transforms des slots et finissent
    /// par causer le bug "icon figée + drag&drop cassé + introuvable".
    /// </summary>
    private void EnsureSlotPool(int count)
    {
        while (_slots.Count < count) {
            var slot = Instantiate(slotTemplate, slotsContainer);
            slot.gameObject.SetActive(false);
            // Autorise le swap visuel pour container↔container et container↔main : le
            // handler ItemSlot.OnItemSwap route ensuite vers C2S_SwapItems.
            slot.CanSwap = true;
            // Pas de stockage imbriqué : refuse en local le drop d'un item-conteneur dans la
            // grille (couvre les deux canaux prop + item, en symétrie avec le garde serveur).
            slot.RejectsStorageItems = true;
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

    /// <summary>
    /// Libère au pool TOUS les <see cref="DraggableItem"/> qui sont enfants directs du
    /// transform du slot. Couvre les orphelins (visual swap hand↔slot qui a reparenté
    /// un draggable non-tracké dans la slot transform).
    /// </summary>
    private static void ReleaseAllDraggableChildren(ItemSlot slot, InventoryUI inv) {
        if (slot == null || inv == null) return;
        var t = slot.transform;
        // Itère en reverse car ReleaseDraggable reparente l'enfant hors du transform
        // → modifie l'index des suivants.
        for (int c = t.childCount - 1; c >= 0; c--) {
            var d = t.GetChild(c).GetComponent<DraggableItem>();
            if (d != null) inv.ReleaseDraggable(d);
        }
    }

    private void ConfigureSlots(string placeId, int count)
    {
        for (int i = 0; i < count && i < _slots.Count; i++) {
            _slots[i].PlaceId   = placeId;
            _slots[i].SlotIndex = i;
        }
    }

    /// <summary>
    /// Calcule la hauteur de grille (rows × cellH + spacing + padding) en fonction de la
    /// configuration du <see cref="UnityEngine.UI.GridLayoutGroup"/> du <see cref="slotsContainer"/>
    /// puis applique cette hauteur au LayoutElement du conteneur de slots et étire le panneau
    /// (<see cref="panelRect"/>) du chrome vertical (titre + bouton de fermeture + paddings).
    /// Tolère l'absence des références sérialisées : skip silencieux.
    /// </summary>
    private void ApplyDynamicHeight(int slotCount)
    {
        if (slotsContainer == null) return;
        var grid = slotsContainer.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid == null) return;

        int cols = grid.constraint == UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount
                 ? Mathf.Max(1, grid.constraintCount)
                 : Mathf.Max(1, slotCount);
        int rows = Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)cols));

        float gridHeight = rows * grid.cellSize.y
                         + Mathf.Max(0, rows - 1) * grid.spacing.y
                         + grid.padding.top
                         + grid.padding.bottom;

        if (slotsLayoutElement != null) {
            slotsLayoutElement.minHeight       = gridHeight;
            slotsLayoutElement.preferredHeight = gridHeight;
            slotsLayoutElement.flexibleHeight  = -1f;
        }
        if (panelRect != null) {
            Vector2 size = panelRect.sizeDelta;
            size.y = gridHeight + verticalChromeHeight;
            panelRect.sizeDelta = size;
        }
    }

    private void ReleaseSpawnedItems()
    {
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;
        foreach (var d in _spawnedItems) {
            if (d == null) continue;
            // Skip items qui ont été déplacés hors de NOS slots : un drag conteneur→main
            // a transféré la propriété du draggable au handSlot (InventoryUI l'a réutilisé
            // pour repeindre la main suite à OnHandChanged). Le re-release ici reparente
            // le draggable au poolContainer et vide visuellement la main alors que
            // handSlot._item le référence toujours → le slot main paraît vide jusqu'au
            // prochain UpdateUI (close/reopen de l'inventaire).
            if (d.ItemSlot != null && !_slots.Contains(d.ItemSlot)) continue;
            if (inv != null) inv.ReleaseDraggable(d);
            else { d.transform.SetParent(transform, false); d.gameObject.SetActive(false); }
        }
        _spawnedItems.Clear();
    }

    private void SpawnItemsFromSnapshot(S2C_ContainerItem[] items)
    {
        if (items == null) return;
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;
        if (inv == null) {
            Debug.LogWarning("[ContainerPanelUI] InventoryUI introuvable — impossible de louer des DraggableItem.");
            return;
        }
        foreach (var entry in items) {
            if (entry.SlotIndex < 0 || entry.SlotIndex >= _slots.Count) continue;

            // Loué depuis le pool partagé InventoryUI : Awake est déjà passé sur ces
            // instances (vivent dans le poolContainer actif de l'inventaire).
            DraggableItem d = inv.RentDraggable();

            if (entry.PropConfigId > 0) {
                // Meuble emballé : vraie icône du prop, non-draggable. Sa sortie passe par
                // le déballage build-mode (clic droit → OnEntryRightClicked).
                var pcfg = DatabaseManager.GetPropsById(entry.PropConfigId);
                d.SetConfiguration(null);
                d.SetSprite(pcfg != null ? pcfg.Sprite : null);
                d.SetDraggable(false);
                d.SetPackedProp(entry.PropConfigId, entry.PropPresetId);
            } else {
                var cfg = DatabaseManager.GetItemConfigById(entry.ConfigId);
                if (cfg == null) {
                    Debug.LogWarning($"[ContainerPanelUI] ItemConfig {entry.ConfigId} introuvable, slot {entry.SlotIndex} ignoré.");
                    inv.ReleaseDraggable(d);
                    continue;
                }
                d.SetConfiguration(cfg);
                d.SetDraggable(true);
            }
            d.SetEntityId(entry.EntityId);
            _slots[entry.SlotIndex].SetItem(d);
            _spawnedItems.Add(d);
        }
    }

    public void Close()
    {
        // Ne ferme QUE ce panneau (son canal). Les deux conteneurs (meuble + colis)
        // peuvent être ouverts en même temps → on ne replie plus tout l'inventaire ici.
        if (NetworkClient.isConnected && _currentPlaceId != null)
            NetworkClient.Send(new C2S_CloseContainer { ItemContainer = IsItemChannel });
        Show(false);
        ReleaseSpawnedItems();
        _currentPlaceId     = null;
        _currentPropId      = 0;
        _currentPropName    = null;
        _loading            = false;
        _autoOpenedEntityId = -1;   // permet de ré-afficher le colis si re-tenu / inventaire ré-ouvert
        SetSlotsInteractive(true);
    }

    /// <summary>
    /// Canal Item uniquement : ré-évalue l'affichage du colis. Appelé à l'ouverture de
    /// l'inventaire pour que la grille du colis réapparaisse si un colis est tenu.
    /// </summary>
    public void RefreshHeldContainer()
    {
        if (IsItemChannel) OnLocalHandsChanged();
    }

    private void Show(bool visible)
    {
        if (root != null) root.SetActive(visible);
    }
}
