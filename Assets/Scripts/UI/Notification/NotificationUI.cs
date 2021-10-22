using DG.Tweening;
using TMPro;
using UnityEngine;

public class NotificationUI : MonoBehaviour {
    private CanvasGroup _canvasGroup;

    private TextMeshProUGUI _messageTxt;

    public static NotificationUI Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
        } else {
            Instance = this;
            this._canvasGroup = GetComponent<CanvasGroup>();
            this._messageTxt = GetComponentInChildren<TextMeshProUGUI>();
            this.Hide(true);
        }
    }

    public void Show(string message) {
        this._messageTxt.text = message;
        this._canvasGroup.DOFade(1, .3f).SetEase(Ease.OutBounce);
        this._canvasGroup.interactable = true;
        this._canvasGroup.blocksRaycasts = true;
    }

    public void Hide(bool instantly) {
        if (instantly) {
            this._canvasGroup.alpha = 0;
        } else {
            this._canvasGroup.DOFade(0, .3f);
        }
        
        this._canvasGroup.interactable = false;
        this._canvasGroup.blocksRaycasts = false;
    }
}