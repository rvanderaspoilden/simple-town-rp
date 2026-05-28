using System.Linq;
using Mirror;
using Sim;
using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private ItemSlot leftHandSlot;
    [SerializeField] private ItemSlot rightHandSlot;
    [SerializeField] private ItemSlot bothHandSlot;
    [SerializeField] private ItemSlot leftPocketSlot;
    [SerializeField] private ItemSlot rightPocketSlot;

    [SerializeField] private Transform     poolContainer;
    [SerializeField] private DraggableItem draggableItemPrefab;

    [SerializeField] private InventoryActionMenu leftHandActionMenu;
    [SerializeField] private InventoryActionMenu rightHandActionMenu;

    [Header("Only for debug")]
    [SerializeField] private InventoryActionMenu currentActionMenu;

    private GenericPool<DraggableItem> _draggableItemPool;

    private void Awake()
    {
        EnsurePool();

        // Les slots mains autorisent le swap hand↔hand (prédiction locale via Swap()).
        // Les slots conteneur gardent CanSwap=false pour éviter la divergence visuel/serveur.
        if (leftHandSlot)  leftHandSlot.CanSwap  = true;
        if (rightHandSlot) rightHandSlot.CanSwap = true;
        if (bothHandSlot)  bothHandSlot.CanSwap  = true;
    }

    /// <summary>
    /// Crée le pool si pas encore fait. Permet à <see cref="RentDraggable"/> /
    /// <see cref="ReleaseDraggable"/> d'être appelés avant le premier OnEnable de
    /// cette UI (ex. ContainerPanelUI loue un draggable alors que le panneau
    /// inventaire n'a jamais été affiché → Awake pas encore passé en hiérarchie inactive).
    /// </summary>
    private void EnsurePool()
    {
        if (_draggableItemPool != null) return;
        _draggableItemPool = new GenericPool<DraggableItem>(
            OnCreateDraggableItem, OnGetDraggableItem, OnReleaseDraggableItem);
    }

    private void OnEnable()
    {
        UpdateUI();

        DraggableItem.OnLeftClick  += OnItemLeftClicked;
        DraggableItem.OnRightClick += OnItemRightClicked;
        DraggableItem.OnStartDrag  += OnItemStartDrag;
        ItemSlot.OnItemMove        += OnItemMoved;
        ItemSlot.OnItemSwap        += OnItemsSwapped;
        PlayerHands.OnHandChanged  += OnPlayerHandChanged;
        ClientItemManager.MoveItemResult += OnMoveItemResultReceived;
    }

    private void OnDisable()
    {
        // Release par slot — NE PAS pool.Dispose() car le pool est aussi consommé par
        // ContainerPanelUI, et un Dispose() global libérerait ses draggables aussi.
        ReleaseSlot(leftHandSlot);
        ReleaseSlot(rightHandSlot);
        ReleaseSlot(bothHandSlot);

        DraggableItem.OnLeftClick  -= OnItemLeftClicked;
        DraggableItem.OnRightClick -= OnItemRightClicked;
        DraggableItem.OnStartDrag  -= OnItemStartDrag;
        ItemSlot.OnItemMove        -= OnItemMoved;
        ItemSlot.OnItemSwap        -= OnItemsSwapped;
        PlayerHands.OnHandChanged  -= OnPlayerHandChanged;
        ClientItemManager.MoveItemResult -= OnMoveItemResultReceived;

        // Ferme le conteneur avec l'inventaire (même comportement que si le joueur
        // clique le bouton fermer du container ou appuie sur Escape).
        ContainerPanelUI.Instance?.Close();
    }

    // ── API pool partagée (consommée par ContainerPanelUI) ────────────────────

    /// <summary>Loue un DraggableItem du pool. Le caller le reparente sur son slot via SetItem.</summary>
    public DraggableItem RentDraggable() { EnsurePool(); return _draggableItemPool.Get(); }

    /// <summary>Rend un DraggableItem au pool (reparente sur le pool container + désactive).</summary>
    public void ReleaseDraggable(DraggableItem item) {
        if (item == null) return;
        EnsurePool();
        _draggableItemPool.Release(item);
    }

    /// <summary>Libère le draggable du slot et clear le slot ; no-op si vide.</summary>
    private void ReleaseSlot(ItemSlot slot) {
        if (slot == null || slot.Item == null) return;
        _draggableItemPool.Release(slot.Item);
        slot.Clear();
    }

    private void Update()
    {
        if (SubGameController.IsActive) return;

        if (Input.GetKeyDown(KeyCode.Escape))
            HUDManager.Instance.CloseInventory();

        // Ferme l'inventaire (et donc le container via OnDisable) dès que le joueur
        // commence à marcher — cohérent avec la règle "HUD fermé pendant le mouvement".
        var agent = PlayerController.Local?.NavMeshAgent;
        if (agent != null && agent.velocity.sqrMagnitude > 0.01f)
            HUDManager.Instance.CloseInventory();
    }

    private void OnPlayerHandChanged()
    {
        Debug.Log("[InventoryUI] [OnPlayerHandChanged] Refreshing all slots");
        CloseCurrentActionMenu(true);
        UpdateUI();
    }

    private void OnItemLeftClicked(DraggableItem draggableItem)   => CloseCurrentActionMenu();
    private void OnItemStartDrag(DraggableItem draggableItem)     => CloseCurrentActionMenu();

    private void OnItemRightClicked(DraggableItem draggableItem)
    {
        if (draggableItem.ItemSlot == leftHandSlot)
        {
            if (currentActionMenu == leftHandActionMenu) CloseCurrentActionMenu();
            else DisplayLeftActionMenu();
        }
        else if (draggableItem.ItemSlot == rightHandSlot || draggableItem.ItemSlot == bothHandSlot)
        {
            // Pour un item TWO_HAND, l'instance est posée en bothHandSlot ;
            // les actions sont sur RightHandItem dans ce cas.
            if (currentActionMenu == rightHandActionMenu) CloseCurrentActionMenu();
            else DisplayRightActionMenu();
        }
    }

    private void OnItemMoved(ItemSlot originSlot, ItemSlot targetSlot)
    {
        // Drop vers un slot de destination différent : route via C2S_MoveItem.
        // Le hand↔hand swap est désormais émis via OnItemSwap (OnItemsSwapped).
        string fromPlace = originSlot.PlaceId;
        string toPlace   = targetSlot.PlaceId;
        if (string.IsNullOrEmpty(fromPlace) || string.IsNullOrEmpty(toPlace)) return;
        // fromPlace == toPlace : hand-swap (left↔right) déjà géré au-dessus ; reorder
        // intra-container (même UUID, slot différent) → laisse passer. Même slot → skip.
        if (fromPlace == toPlace && originSlot.SlotIndex == targetSlot.SlotIndex) return;

        var placeholder = targetSlot.Item;
        int entityId = placeholder != null ? placeholder.EntityId : 0;
        if (entityId <= 0) {
            Debug.LogWarning("[InventoryUI] OnItemMoved : draggable sans EntityId, skip C2S_MoveItem");
            return;
        }
        if (!NetworkClient.isConnected) return;

        NetworkClient.Send(new C2S_MoveItem {
            EntityId    = entityId,
            FromPlaceId = fromPlace,
            ToPlaceId   = toPlace,
            ToSlotIndex = targetSlot.SlotIndex,
        });

        // Le draggable visuellement présent dans targetSlot après OnDrop n'est qu'un placeholder
        // (le même objet du pool, juste reparenté). Le panneau autoritaire (Container ou
        // hand UI) repopulera proprement le slot via le snapshot/S2C_SpawnItem qui suivra.
        // On le rend au pool pour éviter qu'il coexiste avec le draggable de remplacement.
        targetSlot.Clear();
        ReleaseDraggable(placeholder);
    }

    /// <summary>
    /// Swap visuel déjà appliqué dans ItemSlot.OnDrop. On route :
    ///  • hand↔hand   → PlayerHands.Swap() (C2S_RequestSwapHands)
    ///  • le reste    → C2S_SwapItems (un seul aller-retour serveur, deux PATCH atomiques).
    /// Sur échec serveur le snapshot S2C_ContainerOpened re-poussé reconciliera le panneau,
    /// et OnMoveItemResultReceived déclenchera UpdateUI() pour les mains.
    /// </summary>
    private void OnItemsSwapped(ItemSlot slotA, DraggableItem itemA, ItemSlot slotB, DraggableItem itemB)
    {
        bool isHandPair = (slotA == leftHandSlot  && slotB == rightHandSlot)
                       || (slotA == rightHandSlot && slotB == leftHandSlot);
        if (isHandPair) {
            PlayerController.Local.PlayerHands.Swap();
            return;
        }

        if (string.IsNullOrEmpty(slotA.PlaceId) || string.IsNullOrEmpty(slotB.PlaceId)) return;
        int entityA = itemA != null ? itemA.EntityId : 0;
        int entityB = itemB != null ? itemB.EntityId : 0;
        if (entityA <= 0 || entityB <= 0) {
            Debug.LogWarning("[InventoryUI] OnItemsSwapped : entity ids manquants, refresh UI");
            UpdateUI();
            return;
        }
        if (!NetworkClient.isConnected) return;

        NetworkClient.Send(new C2S_SwapItems {
            EntityIdA  = entityA,
            PlaceIdA   = slotA.PlaceId,
            SlotIndexA = slotA.SlotIndex,
            EntityIdB  = entityB,
            PlaceIdB   = slotB.PlaceId,
            SlotIndexB = slotB.SlotIndex,
        });
    }

    private void OnMoveItemResultReceived(S2C_MoveItemResult msg) {
        if (msg.Success) return;
        // Server a rejeté le move : ré-affiche l'état authoritatif des mains.
        // (Le placeholder en target slot a déjà été release, l'origin slot était cleared
        // par OnDrop. UpdateUI re-rente un draggable depuis PlayerHands si l'item y est encore.)
        Debug.LogWarning($"[InventoryUI] Move refusé entity={msg.EntityId} reason={msg.ErrorMessage} — refresh UI");
        UpdateUI();
    }

    public void DisplayLeftActionMenu()
    {
        CloseCurrentActionMenu();
        var item = PlayerController.Local.PlayerHands.LeftHandItem;
        if (item == null) return;
        var actions = item.GetActions().ToList();
        Debug.Log($"[InventoryUI] LeftHand actions count={actions.Count} for config={item.Configuration?.Label}");
        leftHandActionMenu.Setup(actions);
        currentActionMenu = leftHandActionMenu;
    }

    public void DisplayRightActionMenu()
    {
        CloseCurrentActionMenu();
        var item = PlayerController.Local.PlayerHands.RightHandItem;
        if (item == null) return;
        var actions = item.GetActions().ToList();
        Debug.Log($"[InventoryUI] RightHand actions count={actions.Count} for config={item.Configuration?.Label}");
        rightHandActionMenu.Setup(actions);
        currentActionMenu = rightHandActionMenu;
    }

    public void CloseCurrentActionMenu(bool instantly = false)
    {
        if (!currentActionMenu) return;
        currentActionMenu.Hide(instantly);
        currentActionMenu = null;
    }

    public void UpdateUI()
    {
        // Release uniquement les slots mains/poches — pas pool.Dispose() global, qui
        // libérerait aussi les draggables hébergés ailleurs (ex. ContainerPanelUI).
        ReleaseSlot(leftHandSlot);
        ReleaseSlot(rightHandSlot);
        ReleaseSlot(bothHandSlot);

        if (PlayerController.Local == null) return;

        // Pose les PlaceId sur les slots mains/poches dès qu'on connaît le character id.
        // Permet à OnItemMoved de router un drag vers/depuis ces slots via C2S_MoveItem.
        string charId = PlayerController.Local.CharacterData?.Id;
        if (!string.IsNullOrEmpty(charId)) {
            leftHandSlot.PlaceId   = $"hand_left:{charId}";
            rightHandSlot.PlaceId  = $"hand_right:{charId}";
            bothHandSlot.PlaceId   = $"hand_right:{charId}";
            if (leftPocketSlot)  leftPocketSlot.PlaceId  = $"pocket:{charId}";
            if (rightPocketSlot) rightPocketSlot.PlaceId = $"pocket:{charId}";
        }

        var hands = PlayerController.Local.PlayerHands;
        ItemBehaviour rightItem = hands.RightHandItem;
        ItemBehaviour leftItem  = hands.LeftHandItem;
        int leftEntityId  = hands.LeftEntityId;
        int rightEntityId = hands.RightEntityId;

        if (rightItem != null && rightItem.Configuration.HandleType == ItemHandleType.TWO_HAND)
        {
            leftHandSlot.gameObject.SetActive(false);
            rightHandSlot.gameObject.SetActive(false);
            bothHandSlot.gameObject.SetActive(true);

            DraggableItem draggable = _draggableItemPool.Get();
            draggable.SetConfiguration(rightItem.Configuration);
            draggable.SetEntityId(rightEntityId);
            bothHandSlot.SetItem(draggable);
            Debug.Log("[InventoryUI] Refresh slot BothHands (two-hand item)");
        }
        else
        {
            leftHandSlot.gameObject.SetActive(true);
            rightHandSlot.gameObject.SetActive(true);
            bothHandSlot.gameObject.SetActive(false);

            if (leftItem != null)
            {
                DraggableItem draggable = _draggableItemPool.Get();
                draggable.SetConfiguration(leftItem.Configuration);
                draggable.SetEntityId(leftEntityId);
                leftHandSlot.SetItem(draggable);
                Debug.Log("[InventoryUI] Refresh slot LeftHand");
            }

            if (rightItem != null)
            {
                DraggableItem draggable = _draggableItemPool.Get();
                draggable.SetConfiguration(rightItem.Configuration);
                draggable.SetEntityId(rightEntityId);
                rightHandSlot.SetItem(draggable);
                Debug.Log("[InventoryUI] Refresh slot RightHand");
            }
        }

        CloseCurrentActionMenu(true);
    }

    #region Pool Management

    private DraggableItem OnCreateDraggableItem()                   => Instantiate(draggableItemPrefab, poolContainer);
    private void          OnGetDraggableItem(DraggableItem item)    => item.gameObject.SetActive(true);
    private void          OnReleaseDraggableItem(DraggableItem item) { item.ResetDragState(); item.transform.parent = poolContainer; item.gameObject.SetActive(false); }

    #endregion
}
