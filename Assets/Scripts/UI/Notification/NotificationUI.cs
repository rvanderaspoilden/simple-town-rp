using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class NotificationUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
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

    private Action _onClick;

    private bool _setupDone;

    private void Awake() {
        this.EnsureRefs();
        // Instanciée sous un conteneur INACTIF (HUD masqué par un overlay plein écran : cabine
        // d'essayage, boutique…), Awake ne tourne qu'à la réactivation — APRÈS Setup. Ne pas
        // écraser alors l'état posé par Setup (alpha/scale cibles des tweens).
        if (this._setupDone) return;

        this._canvasGroup.alpha = 0;
        this.transform.localScale = Vector3.zero;
    }

    /// <summary>Références composant résolues paresseusement : Setup/RectTransform peuvent être
    /// appelés AVANT Awake quand la notification naît sous un parent inactif.</summary>
    private void EnsureRefs() {
        if (this._canvasGroup == null) this._canvasGroup = GetComponent<CanvasGroup>();
        if (this._rectTransform == null) this._rectTransform = GetComponent<RectTransform>();
    }

    public void Setup(string message, string title, Sprite iconSprite, Action onClick = null) {
        this.EnsureRefs();
        this._setupDone = true;

        this.titleTxt.text = title;
        this.messageTxt.text = message;
        this.icon.sprite = iconSprite;
        this._onClick = onClick;

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

    public RectTransform RectTransform {
        get {
            this.EnsureRefs();
            return this._rectTransform;
        }
    }

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

    public void OnPointerClick(PointerEventData eventData) {
        // Clicks on the close button are consumed by its own Button handler and
        // never reach this method, so we don't need to filter them here.
        if (this._hiding || this._onClick == null) return;

        Action action = this._onClick;
        this._onClick = null; // single-shot guard
        try { action(); }
        catch (Exception e) { Debug.LogError($"[NotificationUI] onClick threw: {e}"); }

        this.Hide();
    }
}
