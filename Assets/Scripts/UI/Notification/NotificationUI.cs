using System.Numerics;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vector3 = UnityEngine.Vector3;

public class NotificationUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private TextMeshProUGUI messageTxt;

    [SerializeField]
    private TextMeshProUGUI titleTxt;

    [SerializeField]
    private Image icon;

    private CanvasGroup _canvasGroup;

    private RectTransform _rectTransform;

    private bool _hiding;

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

        Invoke(nameof(Hide), 10);
    }

    public void Hide() {
        if(this._hiding) return;
        
        this._hiding = true;
        
        this._canvasGroup.DOFade(0, .3f);
        
        this.transform.DOMoveX(this.transform.position.x + this._rectTransform.sizeDelta.x, .5f).OnComplete(() => {
            if (this.transform.parent.childCount > this.transform.GetSiblingIndex() + 1) {
                Transform nextChild = this.transform.parent.GetChild(this.transform.GetSiblingIndex() + 1);
                nextChild.DOMoveY(nextChild.position.y - nextChild.GetComponent<RectTransform>().sizeDelta.y, .3f).OnComplete(() => Destroy(this.gameObject));
            } else {
                Destroy(this.gameObject);
            }
        });
    }
}