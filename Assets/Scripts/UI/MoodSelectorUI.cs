using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class MoodSelectorUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Image arrow;

        [SerializeField]
        private Vector2 moodSizeDelta;

        [SerializeField]
        private Vector2 openedSizeDelta;

        [SerializeField]
        private float openedAnchorX;

        [SerializeField]
        private float openAnimationDuration;

        [SerializeField]
        private float closeAnimationDuration;

        [SerializeField]
        private float showMoodAnimationDuration;

        [SerializeField]
        private float hideMoodAnimationDuration;

        [SerializeField]
        private float moodAnimationDelay;

        [SerializeField]
        private List<RectTransform> moodsTransforms;

        private bool moodSelectorOpened;

        private bool isOpening;

        private RectTransform rectTransform;

        private Vector2 initialSizeDelta;

        private float initialAnchorX;

        private readonly Vector3 negativeScale = new Vector3(-1, 1, 1);

        private void Awake() {
            this.rectTransform = GetComponent<RectTransform>();
            this.initialSizeDelta = this.rectTransform.sizeDelta;
            this.initialAnchorX = this.rectTransform.anchoredPosition.x;
        }

        public void ToggleMoodSelector() {
            if (this.isOpening) {
                return;
            }

            Sequence sequence = DOTween.Sequence();

            this.isOpening = true;

            if (this.moodSelectorOpened) {
                this.moodsTransforms.ForEach(mood => { sequence.Join(mood.DOSizeDelta(Vector2.zero, this.hideMoodAnimationDuration).SetEase(Ease.Linear)); });

                sequence.Join(this.rectTransform.DOAnchorPosX(this.initialAnchorX, this.closeAnimationDuration).SetEase(Ease.Linear));
                sequence.Join(this.rectTransform.DOSizeDelta(this.initialSizeDelta, this.closeAnimationDuration).SetEase(Ease.Linear));

                sequence.OnComplete(() => {
                    this.moodSelectorOpened = false;
                    this.isOpening = false;
                });

                this.arrow.transform.localScale = Vector3.one;
            } else {
                sequence.Join(this.rectTransform.DOAnchorPosX(this.openedAnchorX, this.openAnimationDuration).SetEase(Ease.OutBounce));

                sequence.Join(this.rectTransform.DOSizeDelta(this.openedSizeDelta, this.openAnimationDuration).SetEase(Ease.OutBounce));

                this.moodsTransforms.ForEach(mood => {
                    sequence.Join(mood.DOSizeDelta(this.moodSizeDelta, this.showMoodAnimationDuration)
                        .SetEase(Ease.OutBounce)
                        .From(Vector2.zero)
                        .SetDelay(this.moodAnimationDelay));
                });

                sequence.OnComplete(() => {
                    this.moodSelectorOpened = true;
                    this.isOpening = false;
                });

                this.arrow.transform.localScale = this.negativeScale;
            }

            sequence.Play();
        }
    }
}