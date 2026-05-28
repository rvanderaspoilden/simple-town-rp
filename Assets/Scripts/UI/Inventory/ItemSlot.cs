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
    public delegate void ItemsSwapped(ItemSlot slotA, DraggableItem itemA, ItemSlot slotB, DraggableItem itemB);

    public static event ItemMoved OnItemMove;
    /// <summary>
    /// Émis après un swap visuel. Convention : (<paramref name="slotA"/>, <paramref name="itemA"/>)
    /// décrit l'ORIGINE de l'item draggé (itemA était dans slotA avant le drag),
    /// (<paramref name="slotB"/>, <paramref name="itemB"/>) décrit l'item déplacé qui occupait la
    /// cible (itemB était dans slotB). Après swap : itemA est dans slotB, itemB est dans slotA.
    /// Le handler peut donc envoyer au serveur "déplacer A vers slotB et B vers slotA".
    /// </summary>
    public static event ItemsSwapped OnItemSwap;

    public void OnDrop(PointerEventData eventData) {
        if (eventData.pointerDrag == null) return;

        DraggableItem draggableItem = eventData.pointerDrag.GetComponent<DraggableItem>();

        if (draggableItem) {
            ItemSlot originSlot = draggableItem.ItemSlot;

            if (draggableItem == _item) {
                // Même slot → re-ancre (drag annulé sur soi-même)
                this.SetItem(draggableItem);
            } else if (MustSwapWith(draggableItem)) {
                // CanSwap requis sur les deux slots. hand↔hand est traité localement
                // (PlayerHands.Swap), hand↔container et container↔container passent par
                // C2S_SwapItems côté listener.
                DraggableItem displaced = _item;
                originSlot.SetItem(displaced);
                this.SetItem(draggableItem);
                // Convention origine : (originSlot, draggableItem) = item draggé et sa place
                // d'origine ; (this, displaced) = item qui occupait la cible et sa place d'origine.
                OnItemSwap?.Invoke(originSlot, draggableItem, this, displaced);
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
