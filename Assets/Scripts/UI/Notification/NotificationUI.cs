using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class NotificationUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    [Header("Settings")]
    [SerializeField]
    private TextMeshProUGUI messageTxt;

    [SerializeField]
    private TextMeshProUGUI titleTxt;

    [SerializeField]
    private Image headerImage;

    [SerializeField]
    private Button closeButton;

    [SerializeField]
    private Image icon;

    private CanvasGroup _canvasGroup;

    private RectTransform _rectTransform;

    private bool _hiding;

    private bool _autoHideSet;

    private void Awake() {
        this._canvasGroup = GetComponent<CanvasGroup>();
        this._canvasGroup.alpha = 0;

        this._rectTransform = GetComponent<RectTransform>();

        this.transform.localScale = Vector3.zero;
    }

    public void Setup(string message, NotificationTemplateConfig templateConfig) {
        this.titleTxt.text = templateConfig.Title;
        this.messageTxt.text = message;
        this.icon.sprite = templateConfig.Icon;

        this._canvasGroup.DOFade(1, .3f);
        this.transform.DOScale(Vector3.one, 1f).SetEase(Ease.OutBounce);
    }

    public void Hide() {
        if (this._hiding) return;

        this._hiding = true;

        this._canvasGroup.DOFade(0, .3f);

        NotificationManager.Instance.RemoveNotification(this);
    }

    private void Update() {
        if (!_autoHideSet && this.transform.GetSiblingIndex() == 0 && !_hiding) {
            this.SetAutoHide(2);
        }

        this.closeButton.interactable = !NotificationManager.Instance.IsMovingNotifications;
    }

    public RectTransform RectTransform => _rectTransform;

    public void SetAutoHide(int delay) {
        this._autoHideSet = true;
        Invoke(nameof(Hide), delay);
    }

    public void OnPointerEnter(PointerEventData eventData) {
        this.headerImage.DOComplete();
        this.headerImage.DOFade(.7f, .3f);
    }

    public void OnPointerExit(PointerEventData eventData) {
        this.headerImage.DOComplete();
        this.headerImage.DOFade(0, .3f);
    }
}