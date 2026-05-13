using System.Linq;
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
        PlayerHands.OnHandChanged  += OnPlayerHandChanged;
    }

    private void OnDisable()
    {
        _draggableItemPool.Dispose();

        DraggableItem.OnLeftClick  -= OnItemLeftClicked;
        DraggableItem.OnRightClick -= OnItemRightClicked;
        DraggableItem.OnStartDrag  -= OnItemStartDrag;
        ItemSlot.OnItemMove        -= OnItemMoved;
        PlayerHands.OnHandChanged  -= OnPlayerHandChanged;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
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
        bool leftToRight = originSlot == leftHandSlot  && targetSlot == rightHandSlot;
        bool rightToLeft = originSlot == rightHandSlot && targetSlot == leftHandSlot;
        if (leftToRight || rightToLeft)
            PlayerController.Local.PlayerHands.Swap();
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
        _draggableItemPool.Dispose();

        Debug.Log("[InventoryUI] Clearing previous slot LeftHand");
        leftHandSlot.Clear();
        Debug.Log("[InventoryUI] Clearing previous slot RightHand");
        rightHandSlot.Clear();
        bothHandSlot.Clear();

        if (PlayerController.Local == null) return;

        ItemBehaviour rightItem = PlayerController.Local.PlayerHands.RightHandItem;
        ItemBehaviour leftItem  = PlayerController.Local.PlayerHands.LeftHandItem;

        if (rightItem != null && rightItem.Configuration.HandleType == ItemHandleType.TWO_HAND)
        {
            leftHandSlot.gameObject.SetActive(false);
            rightHandSlot.gameObject.SetActive(false);
            bothHandSlot.gameObject.SetActive(true);

            DraggableItem draggable = _draggableItemPool.Get();
            draggable.SetConfiguration(rightItem.Configuration);
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
                leftHandSlot.SetItem(draggable);
                Debug.Log("[InventoryUI] Refresh slot LeftHand");
            }

            if (rightItem != null)
            {
                DraggableItem draggable = _draggableItemPool.Get();
                draggable.SetConfiguration(rightItem.Configuration);
                rightHandSlot.SetItem(draggable);
                Debug.Log("[InventoryUI] Refresh slot RightHand");
            }
        }

        CloseCurrentActionMenu(true);
    }

    #region Pool Management

    private DraggableItem OnCreateDraggableItem()                   => Instantiate(draggableItemPrefab, poolContainer);
    private void          OnGetDraggableItem(DraggableItem item)    => item.gameObject.SetActive(true);
    private void          OnReleaseDraggableItem(DraggableItem item) { item.transform.parent = poolContainer; item.gameObject.SetActive(false); }

    #endregion
}
