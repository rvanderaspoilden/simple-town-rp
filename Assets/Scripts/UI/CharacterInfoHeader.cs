using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class CharacterInfoHeader : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private CanvasGroup addressCanvasGroup;

    [SerializeField]
    private Image expandButtonImage;

    [SerializeField]
    private Vector2 expandedSizeDelta;

    private bool expanded;

    private Vector2 initialSizeDelta;

    private float initialAnchorY;

    private float expandedAnchorY;

    private bool isAnimating;

    private RectTransform _rectTransform;

    private Sequence _sequence;

    private void Awake() {
        this._rectTransform = GetComponent<RectTransform>();
        this.initialAnchorY = this._rectTransform.anchoredPosition.y;
        this.initialSizeDelta = this._rectTransform.sizeDelta;
        this.expandedAnchorY = this.expandedSizeDelta.y / 2f;


        // Hide address panel by default
        this.addressCanvasGroup.alpha = 0;
    }

    private void OnDestroy() {
        this._sequence.Kill();
    }

    public void Toggle() {
        if (this.isAnimating) return;

        this.expanded = !this.expanded;

        this.isAnimating = true;

        this.expandButtonImage.transform.localScale = new Vector3(1, this.expanded ? -1 : 1, 1);

        if (!this.expanded) {
            this.addressCanvasGroup.DOFade(0, .1f);
        }

        this._sequence = DOTween.Sequence();
        this._sequence.Join(this._rectTransform.DOSizeDelta(this.expanded ? this.expandedSizeDelta : this.initialSizeDelta, .3f));
        this._sequence.Join(this._rectTransform.DOAnchorPosY(this.expanded ? this.expandedAnchorY : this.initialAnchorY, .3f));
        this._sequence.OnComplete(() => {
            if (this.expanded) {
                this.addressCanvasGroup.DOFade(1, .3f);
            }

            this.isAnimating = false;
        });
        this._sequence.Play();
    }
}