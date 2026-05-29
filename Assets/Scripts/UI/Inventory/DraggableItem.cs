using System.Collections;
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

    // ── Animation parameters (tunable per-prefab) ─────────────────────────────
    [Header("Animation — Lift au pickup")]
    [SerializeField, Tooltip("Échelle du draggable pendant un drag.")]
    private float liftScale = 1.08f;
    [SerializeField, Tooltip("Durée de la transition d'échelle (pickup et release).")]
    private float liftDuration = 0.08f;

    [Header("Animation — Land au drop valide")]
    [SerializeField, Tooltip("Durée du tween vers le centre du slot après un drop valide.")]
    private float landDuration = 0.14f;
    [SerializeField, Tooltip("Courbe d'easing du land. EaseOutBack léger donne un effet tactile.")]
    private AnimationCurve landCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 4f),
        new Keyframe(0.7f, 1.06f),
        new Keyframe(1f, 1f, 0f, 0f));

    [Header("Animation — Snap-back drop invalide")]
    [SerializeField, Tooltip("Durée du retour à l'origine quand le drop échoue.")]
    private float snapBackDuration = 0.18f;
    [SerializeField, Tooltip("Courbe d'easing du snap-back. EaseOutQuad pour décélération propre.")]
    private AnimationCurve snapBackCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public float LandDuration         => landDuration;
    public AnimationCurve LandCurve   => landCurve;

    private Coroutine _moveTween;
    private Coroutine _scaleTween;

    public delegate void Draggable(DraggableItem item);

    public static event Draggable OnLeftClick;

    public static event Draggable OnRightClick;

    /// <summary>
    /// Double clic gauche sur le draggable — consommé par les UI d'inventaire pour
    /// router un quick-move (main/poche → conteneur, conteneur → main/poche). Le
    /// premier clic continue de fire OnLeftClick normalement.
    /// </summary>
    public static event Draggable OnDoubleClick;

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
        this._canvasGroup.alpha = .6f;
        this._canvasGroup.blocksRaycasts = false;
        if (this.canvas != null) this.transform.parent = this.canvas.transform;
        this.transform.SetAsLastSibling();
        // Tout tween de position en cours (land/snap-back précédent) doit être stoppé
        // pour ne pas combattre OnDrag qui modifie anchoredPosition à chaque frame.
        if (_moveTween != null) { StopCoroutine(_moveTween); _moveTween = null; }
        AnimateScale(liftScale, liftDuration);
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
                // Unity's EventSystem fournit clickCount sur le second clic d'une
                // séquence (timer interne du StandaloneInputModule). On émet DoubleClick
                // en plus de LeftClick — les handlers existants continuent de marcher.
                if (eventData.clickCount == 2) OnDoubleClick?.Invoke(this);
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
        AnimateScale(1f, liftDuration);

        // Drop out of any slot → snap-back animé vers l'origine.
        // (ItemSlot.OnDrop gère le cas où le drop tombe SUR un slot ; il fire avant
        // OnEndDrag dans l'event order. Si on arrive ici sans qu'aucun slot n'ait
        // attrapé le drop, on revient à l'origine en tween.)
        if (IsOutOfSlot(eventData) && _itemSlot != null) {
            this.transform.parent = this._itemSlot.transform;
            this.SetPadding(10, 10, 10, 10);
            AnimateBackToOrigin();
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
    /// Remet le draggable dans un état neutre (alpha=1, raycast actif, scale=1, tweens stoppés).
    /// À appeler impérativement avant de le rendre au pool : si le GameObject est
    /// désactivé (via SetActive) à l'intérieur du OnDrop, Unity ne déclenche jamais
    /// OnEndDrag sur l'objet désactivé et l'état de drag reste coincé dans le pool.
    /// </summary>
    public void ResetDragState() {
        EnsureRefs();
        if (_canvasGroup == null) return;
        _canvasGroup.alpha = 1f;
        _canvasGroup.blocksRaycasts = true;
        if (_moveTween  != null) { StopCoroutine(_moveTween);  _moveTween  = null; }
        if (_scaleTween != null) { StopCoroutine(_scaleTween); _scaleTween = null; }
        transform.localScale = Vector3.one;
    }

    private bool IsOutOfSlot(PointerEventData eventData) {
        return this._itemSlot && (!eventData.pointerCurrentRaycast.isValid || eventData.pointerCurrentRaycast.gameObject.GetComponent<ItemSlot>() == null);
    }

    // ── Tween helpers (coroutine + AnimationCurve, même pattern que ContainerDoorAnimator) ──

    /// <summary>Anime <c>anchoredPosition</c> de la position courante vers <paramref name="target"/>.
    /// Stoppe tout tween de mouvement en cours.</summary>
    public void AnimateToAnchored(Vector2 target, float duration, AnimationCurve curve) {
        EnsureRefs();
        if (_moveTween != null) { StopCoroutine(_moveTween); _moveTween = null; }
        if (duration <= 0f || !isActiveAndEnabled) {
            _rectTransform.anchoredPosition = target;
            return;
        }
        _moveTween = StartCoroutine(MoveCoroutine(target, duration, curve));
    }

    /// <summary>Anime <c>transform.localScale</c> de manière uniforme vers <paramref name="target"/>.
    /// Stoppe tout tween d'échelle en cours.</summary>
    public void AnimateScale(float target, float duration) {
        EnsureRefs();
        if (_scaleTween != null) { StopCoroutine(_scaleTween); _scaleTween = null; }
        Vector3 endScale = Vector3.one * target;
        if (duration <= 0f || !isActiveAndEnabled) {
            transform.localScale = endScale;
            return;
        }
        _scaleTween = StartCoroutine(ScaleCoroutine(endScale, duration));
    }

    /// <summary>Snap-back animé vers le centre du slot d'accueil (utilise les params snapBack).
    /// Appelé par <see cref="ItemSlot.OnDrop"/> (drop dans un slot occupé non swappable) et par
    /// <see cref="OnEndDrag"/> (drop hors slot).</summary>
    public void AnimateBackToOrigin() {
        AnimateToAnchored(Vector2.zero, snapBackDuration, snapBackCurve);
    }

    private IEnumerator MoveCoroutine(Vector2 target, float duration, AnimationCurve curve) {
        Vector2 start = _rectTransform.anchoredPosition;
        float t = 0f;
        while (t < duration) {
            // unscaledDeltaTime : indépendant de Time.timeScale (UI ne doit pas geler en pause).
            t += Time.unscaledDeltaTime;
            float k = curve.Evaluate(Mathf.Clamp01(t / duration));
            _rectTransform.anchoredPosition = Vector2.LerpUnclamped(start, target, k);
            yield return null;
        }
        _rectTransform.anchoredPosition = target;
        _moveTween = null;
    }

    private IEnumerator ScaleCoroutine(Vector3 target, float duration) {
        Vector3 start = transform.localScale;
        float t = 0f;
        while (t < duration) {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            transform.localScale = Vector3.LerpUnclamped(start, target, k);
            yield return null;
        }
        transform.localScale = target;
        _scaleTween = null;
    }
}