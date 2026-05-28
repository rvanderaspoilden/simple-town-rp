using UnityEngine;
using UnityEngine.EventSystems;

public class ItemSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler {
    private DraggableItem _item;

    // Place backend de ce slot (hand_left:..., hand_right:..., pocket:..., container place id).
    // Sert au routage cross-place via C2S_MoveItem (InventoryUI.OnItemMoved).
    private string _placeId;
    public string PlaceId {
        get => _placeId;
        set => _placeId = value;
    }

    // Index dans la grille du conteneur ; ignoré pour mains/poches (mono-slot).
    private int _slotIndex;
    public int SlotIndex {
        get => _slotIndex;
        set => _slotIndex = value;
    }

    // Seuls les slots mains autorisent le swap visuel immédiat (hand↔hand).
    // Les slots conteneur ne le font pas : le serveur ne supporte pas l'échange
    // atomique, et le snap-back évite une divergence visuel/serveur.
    public bool CanSwap { get; set; } = false;

    public delegate void ItemMoved(ItemSlot origin, ItemSlot target);

    public static event ItemMoved OnItemMove;

    public void OnDrop(PointerEventData eventData) {
        if (eventData.pointerDrag == null) return;

        DraggableItem draggableItem = eventData.pointerDrag.GetComponent<DraggableItem>();

        if (draggableItem) {
            ItemSlot originSlot = draggableItem.ItemSlot;

            if (draggableItem == _item) {
                // Même slot → re-ancre (drag annulé sur soi-même)
                this.SetItem(draggableItem);
            } else if (MustSwapWith(draggableItem)) {
                // Swap hand↔hand uniquement (CanSwap requis sur les deux slots)
                draggableItem.ItemSlot.SetItem(_item);
                this.SetItem(draggableItem);
                OnItemMove?.Invoke(originSlot, this);
            } else if (!this._item) {
                // Slot vide → déplace
                draggableItem.ItemSlot.Clear();
                this.SetItem(draggableItem);
                OnItemMove?.Invoke(originSlot, this);
            } else {
                // Slot occupé sans swap autorisé (container↔main ou intra-container) →
                // snap back à l'origine pour éviter divergence visuel/serveur.
                if (originSlot != null) originSlot.SetItem(draggableItem);
            }
        }
    }

    public void SetItem(DraggableItem item) {
        this._item = item;
        this._item.transform.parent = this.transform;
        this._item.SetAnchoredPosition(Vector2.zero);
        this._item.SetPadding(10, 10, 10, 10);
        this._item.ItemSlot = this;
    }

    public DraggableItem Item => _item;

    public void Clear() {
        this._item = null;
    }

    private bool MustSwapWith(DraggableItem draggableItem) {
        if (!CanSwap) return false;
        var originSlot = draggableItem.ItemSlot;
        if (originSlot == null || !originSlot.CanSwap) return false;
        return this._item && this._item != draggableItem;
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (eventData.pointerDrag == null) return;

        Debug.Log("HOVER");
    }
}
