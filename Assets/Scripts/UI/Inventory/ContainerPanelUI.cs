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
public class ContainerPanelUI : MonoBehaviour
{
    public static ContainerPanelUI Instance { get; private set; }

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
    private string _currentPropName;     // cache pour le titre, valorisé à l'ouverture optimiste
    private bool   _subscribed;
    private bool   _loading;
    private UnityEngine.CanvasGroup _slotsGroup;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

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
        if (Instance == this) Instance = null;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        ClientItemManager.ContainerOpened     += OnContainerOpened;
        ClientItemManager.ContainerOpenFailed += OnContainerOpenFailed;
        StorageContainerBehaviour.OnOpenRequested += OnOptimisticOpenRequested;
        DraggableItem.OnDoubleClick           += OnItemDoubleClicked;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        ClientItemManager.ContainerOpened     -= OnContainerOpened;
        ClientItemManager.ContainerOpenFailed -= OnContainerOpenFailed;
        StorageContainerBehaviour.OnOpenRequested -= OnOptimisticOpenRequested;
        DraggableItem.OnDoubleClick           -= OnItemDoubleClicked;
        _subscribed = false;
    }

    /// <summary>True quand une session conteneur est ouverte ET prête (snapshot reçu).
    /// Tant que <see cref="_loading"/> est vrai, le PlaceId backend n'est pas connu et les
    /// quick-moves doivent attendre.</summary>
    public bool IsOpen => !_loading && !string.IsNullOrEmpty(_currentPlaceId);

    /// <summary>UUID backend de la place conteneur en cours. Utilisé pour router un quick-move.</summary>
    public string PlaceId => _currentPlaceId;

    /// <summary>Premier slot actif vide, ou -1 si plein.</summary>
    public int FindFirstFreeSlot()
    {
        for (int i = 0; i < _slots.Count; i++) {
            var slot = _slots[i];
            if (slot != null && slot.gameObject.activeSelf && slot.Item == null) return i;
        }
        return -1;
    }

    /// <summary>True si <paramref name="slot"/> appartient à la grille de ce conteneur.</summary>
    public bool OwnsSlot(ItemSlot slot) => slot != null && _slots.Contains(slot);

    private void OnItemDoubleClicked(DraggableItem item)
    {
        if (!IsOpen || item == null || item.ItemSlot == null) return;
        if (!OwnsSlot(item.ItemSlot)) return;

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
        _currentPropId   = propId;
        _currentPropName = propName;
        _currentPlaceId  = null;    // sera renseigné par le snapshot
        _loading         = true;

        Show(true);
        if (HUDManager.Instance != null) HUDManager.Instance.ShowInventory();

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
        _currentPlaceId = msg.PlaceId;
        _currentPropId  = msg.PropId;

        // Show AVANT de spawner : sinon les nouveaux DraggableItem sont instanciés
        // dans une hiérarchie inactive et leur Awake ne tourne pas → _image null
        // → NRE dès SetConfiguration. Show(true) active root + slots.
        // Idempotent quand on vient d'OpenOptimistic (déjà Show(true) + ShowInventory).
        Show(true);
        if (HUDManager.Instance != null) HUDManager.Instance.ShowInventory();

        EnsureSlotPool(msg.SlotCount);
        ConfigureSlots(msg.PlaceId, msg.SlotCount);
        ApplyDynamicHeight(msg.SlotCount);
        ReleaseSpawnedItems();
        if (titleText != null) titleText.text = ResolveTitle();
        SpawnItemsFromSnapshot(msg.Items);

        // Snapshot reçu → panneau pleinement interactif.
        _loading = false;
        SetSlotsInteractive(true);
    }

    private void OnContainerOpenFailed(S2C_ContainerOpenFailed msg)
    {
        Debug.LogWarning($"[ContainerPanelUI] Ouverture refusée propId={msg.PropId} : {msg.ErrorMessage}");
        // Si le panneau était ouvert optimistement pour ce prop, on referme + toast.
        if (_loading && _currentPropId == msg.PropId) {
            if (!string.IsNullOrEmpty(msg.ErrorMessage)) WorldToastManager.Show(msg.ErrorMessage);
            Close();
        }
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
            var cfg = DatabaseManager.GetItemConfigById(entry.ConfigId);
            if (cfg == null) {
                Debug.LogWarning($"[ContainerPanelUI] ItemConfig {entry.ConfigId} introuvable, slot {entry.SlotIndex} ignoré.");
                continue;
            }
            // Loué depuis le pool partagé InventoryUI : Awake est déjà passé sur ces
            // instances (vivent dans le poolContainer actif de l'inventaire).
            DraggableItem d = inv.RentDraggable();
            d.SetConfiguration(cfg);
            d.SetEntityId(entry.EntityId);
            _slots[entry.SlotIndex].SetItem(d);
            _spawnedItems.Add(d);
        }
    }

    public void Close()
    {
        // Ne notifie le serveur que si une session est réellement ouverte.
        if (NetworkClient.isConnected && _currentPlaceId != null)
            NetworkClient.Send(new C2S_CloseContainer());
        Show(false);
        ReleaseSpawnedItems();
        _currentPlaceId  = null;
        _currentPropId   = 0;
        _currentPropName = null;
        _loading         = false;
        SetSlotsInteractive(true);

        // Ferme aussi l'inventaire pour aligner conteneur et HUD : clic « X »
        // ⇒ plus aucune interaction prop, HUD complètement repliée. L'OnDisable
        // de InventoryUI rappellera Close() en cascade, mais les états ci-dessus
        // sont déjà nettoyés → re-entrée idempotente.
        if (HUDManager.Instance != null) HUDManager.Instance.CloseInventory();
    }

    private void Show(bool visible)
    {
        if (root != null) root.SetActive(visible);
    }
}
