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

    private readonly List<ItemSlot> _slots = new List<ItemSlot>();
    private readonly List<DraggableItem> _spawnedItems = new List<DraggableItem>();

    private string _currentPlaceId;
    private int    _currentPropId;
    private bool   _subscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (slotTemplate != null) slotTemplate.gameObject.SetActive(false);
        if (closeButton  != null) closeButton.onClick.AddListener(Close);

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
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        ClientItemManager.ContainerOpened     -= OnContainerOpened;
        ClientItemManager.ContainerOpenFailed -= OnContainerOpenFailed;
        _subscribed = false;
    }

    private void OnContainerOpened(S2C_ContainerOpened msg)
    {
        _currentPlaceId = msg.PlaceId;
        _currentPropId  = msg.PropId;

        // Show AVANT de spawner : sinon les nouveaux DraggableItem sont instanciés
        // dans une hiérarchie inactive et leur Awake ne tourne pas → _image null
        // → NRE dès SetConfiguration. Show(true) active root + slots.
        Show(true);
        if (HUDManager.Instance != null) HUDManager.Instance.ShowInventory();

        EnsureSlotPool(msg.SlotCount);
        ConfigureSlots(msg.PlaceId, msg.SlotCount);
        ReleaseSpawnedItems();
        if (titleText != null) titleText.text = $"Conteneur ({msg.SlotCount} slots)";
        SpawnItemsFromSnapshot(msg.Items);
    }

    private void OnContainerOpenFailed(S2C_ContainerOpenFailed msg)
    {
        Debug.LogWarning($"[ContainerPanelUI] Ouverture refusée propId={msg.PropId} : {msg.ErrorMessage}");
    }

    /// <summary>
    /// Garantit qu'il y a <paramref name="count"/> slots clonés depuis le template.
    /// Les slots existants en surplus sont désactivés.
    /// </summary>
    private void EnsureSlotPool(int count)
    {
        while (_slots.Count < count) {
            var slot = Instantiate(slotTemplate, slotsContainer);
            slot.gameObject.SetActive(false);
            _slots.Add(slot);
        }
        for (int i = 0; i < _slots.Count; i++) {
            _slots[i].gameObject.SetActive(i < count);
            if (i < count) _slots[i].Clear();
        }
    }

    private void ConfigureSlots(string placeId, int count)
    {
        for (int i = 0; i < count && i < _slots.Count; i++) {
            _slots[i].PlaceId   = placeId;
            _slots[i].SlotIndex = i;
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
        _currentPlaceId = null;
        _currentPropId  = 0;
    }

    private void Show(bool visible)
    {
        if (root != null) root.SetActive(visible);
    }
}
