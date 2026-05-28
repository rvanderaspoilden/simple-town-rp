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
        EnsureRefs();
    }

    /// <summary>
    /// Cache les composants nécessaires. Appelé idempotemment depuis chaque entry-point
    /// public (SetConfiguration, OnBeginDrag, …) car ces draggables peuvent être
    /// instanciés dans une hiérarchie inactive (parent désactivé) — auquel cas Awake
    /// ne tourne PAS et tous les champs cachés sont null jusqu'à l'activation. Cette
    /// méthode est l'auto-réparation : zéro coût si déjà initialisé.
    /// </summary>
    private void EnsureRefs() {
        if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
        if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
        if (_image == null) _image = GetComponent<Image>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
    }

    // EntityId côté serveur (utilisé pour C2S_MoveItem). 0 = inconnu / pas encore mappé.
    private int _entityId;
    public int EntityId => _entityId;
    public void SetEntityId(int entityId) => _entityId = entityId;

    public void SetConfiguration(ItemConfig itemConfig) {
        EnsureRefs();
        this._itemConfig = itemConfig;
        this._image.sprite = this._itemConfig != null ? this._itemConfig.Icon : null;
    }

    public void OnBeginDrag(PointerEventData eventData) {
        EnsureRefs();
        Debug.Log("Start drag");
        this._canvasGroup.alpha = .6f;
        this._canvasGroup.blocksRaycasts = false;
        if (this.canvas != null) this.transform.parent = this.canvas.transform;
        this.transform.SetAsLastSibling();
        OnStartDrag?.Invoke(this);
    }

    public void OnDrag(PointerEventData eventData) {
        EnsureRefs();
        if (canvas == null) return;
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
        EnsureRefs();
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
        EnsureRefs();
        this._rectTransform.anchoredPosition = pos;
    }

    public void SetPadding(float top, float right, float bottom, float left) {
        EnsureRefs();
        this._rectTransform.offsetMin = new Vector2(left, bottom);
        this._rectTransform.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>
    /// Remet le draggable dans un état neutre (alpha=1, raycast actif).
    /// À appeler impérativement avant de le rendre au pool : si le GameObject est
    /// désactivé (via SetActive) à l'intérieur du OnDrop, Unity ne déclenche jamais
    /// OnEndDrag sur l'objet désactivé et l'état de drag reste coincé dans le pool.
    /// </summary>
    public void ResetDragState() {
        EnsureRefs();
        if (_canvasGroup == null) return;
        _canvasGroup.alpha = 1f;
        _canvasGroup.blocksRaycasts = true;
    }

    private bool IsOutOfSlot(PointerEventData eventData) {
        return this._itemSlot && (!eventData.pointerCurrentRaycast.isValid || eventData.pointerCurrentRaycast.gameObject.GetComponent<ItemSlot>() == null);
    }
}