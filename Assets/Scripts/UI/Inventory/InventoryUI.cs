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
        Debug.Log("[InventoryUI] [OnPlayerChanged]");
        CloseCurrentActionMenu();
        HUDManager.Instance.InventoryUI.Invoke(nameof(UpdateUI), .1f);
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
        else if (draggableItem.ItemSlot == rightHandSlot)
        {
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
        if (PlayerController.Local.PlayerHands.LeftHandItem == null) return;
        leftHandActionMenu.Setup(PlayerController.Local.PlayerHands.LeftHandItem.GetActions().ToList());
        currentActionMenu = leftHandActionMenu;
    }

    public void DisplayRightActionMenu()
    {
        CloseCurrentActionMenu();
        if (PlayerController.Local.PlayerHands.RightHandItem == null) return;
        rightHandActionMenu.Setup(PlayerController.Local.PlayerHands.RightHandItem.GetActions().ToList());
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
        leftHandSlot.Clear();
        rightHandSlot.Clear();
        bothHandSlot.Clear();

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
            }

            if (rightItem != null)
            {
                DraggableItem draggable = _draggableItemPool.Get();
                draggable.SetConfiguration(rightItem.Configuration);
                rightHandSlot.SetItem(draggable);
            }
            else if (rightHandSlot.Item)
            {
                _draggableItemPool.Release(rightHandSlot.Item);
                rightHandSlot.Clear();
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
