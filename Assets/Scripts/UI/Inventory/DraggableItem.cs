using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DraggableItem : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IEndDragHandler, IDragHandler {
    private Canvas canvas;
    private Image _image;
    private RectTransform _rectTransform;
    private CanvasGroup _canvasGroup;
    private ItemSlot _itemSlot;
    private ItemConfig _itemConfig;

    public delegate void Draggable(DraggableItem item);

    public static event Draggable OnLeftClick;

    public static event Draggable OnRightClick;
    
    public static event Draggable OnStartDrag;

    private void Awake() {
        this._rectTransform = GetComponent<RectTransform>();
        this._canvasGroup = GetComponent<CanvasGroup>();
        this._image = GetComponent<Image>();
        this.canvas = GetComponentInParent<Canvas>();
    }

    public void SetConfiguration(ItemConfig itemConfig) {
        this._itemConfig = itemConfig;
        this._image.sprite = this._itemConfig.Icon;
    }

    public void OnBeginDrag(PointerEventData eventData) {
        Debug.Log("Start drag");
        this._canvasGroup.alpha = .6f;
        this._canvasGroup.blocksRaycasts = false;
        this.transform.parent = this.canvas.transform;
        this.transform.SetAsLastSibling();
        OnStartDrag?.Invoke(this);
    }

    public void OnDrag(PointerEventData eventData) {
        this._rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
    }

    public void OnPointerClick(PointerEventData eventData) {
        switch (eventData.button) {
            case PointerEventData.InputButton.Left:
                OnLeftClick?.Invoke(this);
                break;

            case PointerEventData.InputButton.Right:
                OnRightClick?.Invoke(this);
                break;
        }
    }

    public void OnEndDrag(PointerEventData eventData) {
        this._canvasGroup.alpha = 1;
        this._canvasGroup.blocksRaycasts = true;

        // Manage drop out of slot
        if (IsOutOfSlot(eventData)) {
            this.transform.parent = this._itemSlot.transform;
            this.SetAnchoredPosition(Vector2.zero);
        }
    }

    public ItemSlot ItemSlot {
        get => _itemSlot;
        set => _itemSlot = value;
    }

    public ItemConfig ItemConfig => _itemConfig;

    public void SetAnchoredPosition(Vector2 pos) {
        this._rectTransform.anchoredPosition = pos;
    }

    public void SetPadding(float top, float right, float bottom, float left) {
        this._rectTransform.offsetMin = new Vector2(left, bottom);
        this._rectTransform.offsetMax = new Vector2(-right, -top);
    }

    private bool IsOutOfSlot(PointerEventData eventData) {
        return this._itemSlot && (!eventData.pointerCurrentRaycast.isValid || eventData.pointerCurrentRaycast.gameObject.GetComponent<ItemSlot>() == null);
    }
}