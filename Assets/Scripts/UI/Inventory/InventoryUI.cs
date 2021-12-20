using System;
using System.Linq;
using Sim;
using UnityEngine;

public class InventoryUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private ItemSlot leftHandSlot;

    [SerializeField]
    private ItemSlot rightHandSlot;

    [SerializeField]
    private ItemSlot bothHandSlot;

    [SerializeField]
    private ItemSlot leftPocketSlot;

    [SerializeField]
    private ItemSlot rightPocketSlot;

    [SerializeField]
    private Transform poolContainer;

    [SerializeField]
    private DraggableItem draggableItemPrefab;
    
    [SerializeField]
    private InventoryActionMenu leftHandActionMenu;

    [SerializeField]
    private InventoryActionMenu rightHandActionMenu;

    [Header("Only for debug")]
    [SerializeField]
    private InventoryActionMenu currentActionMenu;

    private GenericPool<DraggableItem> _draggableItemPool;

    private void Awake() {
        this._draggableItemPool = new GenericPool<DraggableItem>(OnCreateDraggableItem, OnGetDraggableItem, OnReleaseDraggableItem);
    }
    
    private void OnEnable() {
        this.UpdateUI();

        DraggableItem.OnClick += OnItemClicked;
        ItemSlot.OnItemMove += OnItemMoved;
    }

    private void OnDisable() {
        this._draggableItemPool.Dispose();
        
        DraggableItem.OnClick -= OnItemClicked;
        ItemSlot.OnItemMove -= OnItemMoved;
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Escape)) {
            HUDManager.Instance.CloseInventory();
        }
    }

    private void OnItemClicked(DraggableItem draggableItem) {
        this.CloseCurrentActionMenu();
    }

    private void OnItemMoved(ItemSlot originSlot, ItemSlot targetSlot) {
        if ((originSlot == this.leftHandSlot && targetSlot == this.rightHandSlot) || (originSlot == this.rightHandSlot && targetSlot == this.leftHandSlot)) {
            PlayerController.Local.PlayerHands.Swap();
        } 
    }
    
    public void DisplayLeftActionMenu() {
        this.CloseCurrentActionMenu();

        if (PlayerController.Local.PlayerHands.LeftHandItem == null) return;

        this.leftHandActionMenu.Setup(PlayerController.Local.PlayerHands.LeftHandItem.GetActions().ToList());

        this.currentActionMenu = this.leftHandActionMenu;
    }

    public void DisplayRightActionMenu() {
        this.CloseCurrentActionMenu();

        if (PlayerController.Local.PlayerHands.RightHandItem == null) return;

        this.rightHandActionMenu.Setup(PlayerController.Local.PlayerHands.RightHandItem.GetActions().ToList());

        this.currentActionMenu = this.rightHandActionMenu;
    }

    public void CloseCurrentActionMenu(bool instantly = false) {
        if (!this.currentActionMenu) return;

        this.currentActionMenu.Hide(instantly);
    }

    public void UpdateUI() {
        if (PlayerController.Local.PlayerHands.RightHandItem && 
            PlayerController.Local.PlayerHands.RightHandItem.Configuration.HandleType == ItemHandleType.TWO_HAND) {
            this.leftHandSlot.gameObject.SetActive(false);
            this.rightHandSlot.gameObject.SetActive(false);
            this.bothHandSlot.gameObject.SetActive(true);

            DraggableItem draggableItem = this._draggableItemPool.Get();
            draggableItem.SetConfiguration(PlayerController.Local.PlayerHands.RightHandItem.Configuration);
            this.bothHandSlot.SetItem(draggableItem);
        } else {
            this.leftHandSlot.gameObject.SetActive(true);
            this.rightHandSlot.gameObject.SetActive(true);
            this.bothHandSlot.gameObject.SetActive(false);

            if (PlayerController.Local.PlayerHands.LeftHandItem) {
                DraggableItem draggableItem = this._draggableItemPool.Get();
                draggableItem.SetConfiguration(PlayerController.Local.PlayerHands.LeftHandItem.Configuration);
                this.leftHandSlot.SetItem(draggableItem);
            } 

            if (PlayerController.Local.PlayerHands.RightHandItem) {
                DraggableItem draggableItem = this._draggableItemPool.Get();
                draggableItem.SetConfiguration(PlayerController.Local.PlayerHands.RightHandItem.Configuration);
                this.rightHandSlot.SetItem(draggableItem);
            } 
        }

        this.CloseCurrentActionMenu(true);
    }
    
    #region Pool Management

    private DraggableItem OnCreateDraggableItem() {
        DraggableItem draggableItem = Instantiate(this.draggableItemPrefab, this.poolContainer);
        return draggableItem;
    } 
    
    private void OnGetDraggableItem(DraggableItem draggableItem) {
        draggableItem.gameObject.SetActive(true);
    }

    private void OnReleaseDraggableItem(DraggableItem draggableItem) {
        draggableItem.transform.parent = this.poolContainer;
        draggableItem.gameObject.SetActive(false);
    }

    #endregion

}