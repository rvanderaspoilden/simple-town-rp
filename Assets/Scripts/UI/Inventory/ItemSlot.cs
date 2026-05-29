using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler {
    private DraggableItem _item;

    [Header("Hover highlight (optionnel)")]
    [SerializeField, Tooltip("Graphic (Image background, overlay…) qui fade-in quand un draggable survole ce slot. Laisser vide pour désactiver.")]
    private Graphic dropHighlight;
    [SerializeField, Range(0f, 1f), Tooltip("Alpha cible du highlight survol.")]
    private float highlightAlpha = 0.3f;
    [SerializeField, Tooltip("Durée du fade in/out (secondes).")]
    private float highlightFadeDuration = 0.1f;

    private Coroutine _fadeTween;

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
        if (!draggableItem) return;

        // Le drag se termine ici (sur ce slot ou un autre) — hide highlight immédiat.
        FadeHighlight(0f);

        ItemSlot originSlot = draggableItem.ItemSlot;

        if (draggableItem == _item) {
            // Même slot → re-ancre animé (drag annulé sur soi-même).
            this.SetItem(draggableItem, animate: true);
        } else if (MustSwapWith(draggableItem)) {
            // CanSwap requis sur les deux slots. hand↔hand est traité localement
            // (PlayerHands.Swap), hand↔container et container↔container passent par
            // C2S_SwapItems côté listener. Les deux items tweenent en parallèle.
            DraggableItem displaced = _item;
            originSlot.SetItem(displaced, animate: true);
            this.SetItem(draggableItem, animate: true);
            OnItemSwap?.Invoke(originSlot, draggableItem, this, displaced);
        } else if (!this._item) {
            // Slot vide → déplace, smooth land.
            draggableItem.ItemSlot.Clear();
            this.SetItem(draggableItem, animate: true);
            OnItemMove?.Invoke(originSlot, this);
        } else {
            // Slot occupé sans swap autorisé → snap-back animé vers l'origine.
            if (originSlot != null) originSlot.SnapBackInto(draggableItem);
        }
    }

    public void SetItem(DraggableItem item) => SetItem(item, animate: false);

    /// <summary>
    /// Place <paramref name="item"/> dans ce slot. Si <paramref name="animate"/> est vrai,
    /// l'item glisse en monde vers le centre du slot (sensation de drop tactile) ; sinon
    /// snap instantané (utilisé par les reconstructions de snapshot serveur).
    ///
    /// Pour l'anim, on utilise <c>SetParent(…, worldPositionStays:true)</c> pour ne pas
    /// teleporter au moment du change-parent, puis l'item tween jusqu'à
    /// <c>this.transform.position</c> ; SetPadding est appliqué dans le callback de fin
    /// pour ne pas snape avant l'arrivée. Le chemin non animé garde l'ancien comportement.
    /// </summary>
    public void SetItem(DraggableItem item, bool animate) {
        this._item = item;
        this._item.ItemSlot = this;

        if (animate) {
            Vector3 targetWorld = this.transform.position;
            item.transform.SetParent(this.transform, worldPositionStays: true);
            item.AnimateToWorldPosition(targetWorld, item.LandDuration, item.LandCurve, onComplete: () => {
                if (item == null || this == null || item.ItemSlot != this) return;
                item.SetPadding(10, 10, 10, 10);
                item.SetAnchoredPosition(Vector2.zero);
            });
        } else {
            item.transform.parent = this.transform;
            item.SetPadding(10, 10, 10, 10);
            item.SetAnchoredPosition(Vector2.zero);
        }
    }

    /// <summary>
    /// Snap-back animé : l'item revient à ce slot (son origine) après un drop invalide.
    /// Utilise <see cref="DraggableItem.SnapBackDuration"/> / <see cref="DraggableItem.SnapBackCurve"/>.
    /// </summary>
    public void SnapBackInto(DraggableItem item) {
        this._item = item;
        this._item.ItemSlot = this;

        Vector3 targetWorld = this.transform.position;
        item.transform.SetParent(this.transform, worldPositionStays: true);
        item.AnimateToWorldPosition(targetWorld, item.SnapBackDuration, item.SnapBackCurve, onComplete: () => {
            if (item == null || this == null || item.ItemSlot != this) return;
            item.SetPadding(10, 10, 10, 10);
            item.SetAnchoredPosition(Vector2.zero);
        });
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

    private void Awake() {
        // Démarre invisible — sinon un highlight rouge oublié dans le prefab resterait
        // affiché en permanence.
        if (dropHighlight != null) {
            Color c = dropHighlight.color;
            c.a = 0f;
            dropHighlight.color = c;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (eventData.pointerDrag == null) return;
        // Seuls les drags d'un DraggableItem déclenchent le highlight (ignore les
        // pointerDrag d'autres systèmes UI éventuels).
        if (eventData.pointerDrag.GetComponent<DraggableItem>() == null) return;
        FadeHighlight(highlightAlpha);
    }

    public void OnPointerExit(PointerEventData eventData) {
        // Pendant un drag, on hide quand le pointeur sort. Hors drag, no-op (l'alpha
        // est déjà 0).
        if (eventData.pointerDrag == null) return;
        FadeHighlight(0f);
    }

    private void FadeHighlight(float targetAlpha) {
        if (dropHighlight == null) return;
        if (_fadeTween != null) { StopCoroutine(_fadeTween); _fadeTween = null; }
        if (highlightFadeDuration <= 0f || !isActiveAndEnabled) {
            Color c = dropHighlight.color;
            c.a = targetAlpha;
            dropHighlight.color = c;
            return;
        }
        _fadeTween = StartCoroutine(FadeHighlightCoroutine(targetAlpha));
    }

    private IEnumerator FadeHighlightCoroutine(float targetAlpha) {
        Color start = dropHighlight.color;
        float t = 0f;
        while (t < highlightFadeDuration) {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / highlightFadeDuration);
            Color c = start;
            c.a = Mathf.Lerp(start.a, targetAlpha, k);
            dropHighlight.color = c;
            yield return null;
        }
        Color end = dropHighlight.color;
        end.a = targetAlpha;
        dropHighlight.color = end;
        _fadeTween = null;
    }
}
