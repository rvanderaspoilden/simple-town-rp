using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

namespace Sim {
    public class DefaultViewUI : MonoBehaviour {
        [SerializeField]
        private RectTransform phone;

        [SerializeField]
        private RectTransform moodSelector;

        [SerializeField]
        private List<RectTransform> moods;

        private bool phoneOpened;
        private bool moodSelectorOpened;
        
        private Vector2 firstPressPos;
        private Vector2 secondPressPos;
        private Vector2 currentSwipe;
        private float defaultPhoneAnchorPosY;

        private void Awake() {
            this.defaultPhoneAnchorPosY = phone.anchoredPosition.y;
            this.moods.ForEach(mood => mood.sizeDelta = Vector2.zero);
        }

        private void Update() {
            this.Swipe();
        }

        public void ToggleMoodSelector() {
            Sequence sequence = DOTween.Sequence();

            if (this.moodSelectorOpened) {
                this.moods.ForEach(mood => {
                    sequence.Join(mood.DOSizeDelta(new Vector2(0, 0), 0.1f).SetEase(Ease.Linear));
                });
                
                sequence.Join(this.moodSelector.DOAnchorPosX(17.90002f, 0.2f).SetEase(Ease.Linear));
                sequence.Join(this.moodSelector.DOSizeDelta(new Vector2(32.59509f, this.moodSelector.sizeDelta.y), 0.2f).SetEase(Ease.Linear));

                sequence.OnComplete(() => this.moodSelectorOpened = false);
            } else {
                sequence.Join(this.moodSelector.DOAnchorPosX(165.56f, 0.3f).SetEase(Ease.OutBounce));

                sequence.Join(this.moodSelector.DOSizeDelta(new Vector2(328.2852f, this.moodSelector.sizeDelta.y), 0.3f).SetEase(Ease.OutBounce));
                
                this.moods.ForEach(mood => {
                    sequence.Join(mood.DOSizeDelta(new Vector2(50, 50), 1f).SetEase(Ease.InOutElastic).From(Vector2.zero).SetDelay(0.05f));
                });

                sequence.OnComplete(() => this.moodSelectorOpened = true);
            }
            
            sequence.Play();
        }

        public void OpenPhone() {
            if (!this.phoneOpened) {
                this.phone.DOAnchorPosY(0, 0.5f).SetEase(Ease.Flash).OnComplete(() => this.phoneOpened = true);
            }
        }

        public void ClosePhone() {
            if (this.phoneOpened) {
                this.phone.DOAnchorPosY(this.defaultPhoneAnchorPosY, 1).SetEase(Ease.OutBounce).OnComplete(() => this.phoneOpened = false);
            }
        }
        
        // TODO: it's temporary
        public void Swipe()
        {
            if(Input.GetMouseButtonDown(0))
            {
                firstPressPos = new Vector2(Input.mousePosition.x,Input.mousePosition.y);
            }
            if(Input.GetMouseButtonUp(0))
            {
                secondPressPos = new Vector2(Input.mousePosition.x,Input.mousePosition.y);
       
                currentSwipe = new Vector2(secondPressPos.x - firstPressPos.x, secondPressPos.y - firstPressPos.y);
           
                currentSwipe.Normalize();
 
                //swipe upwards
                if(currentSwipe.y > 0 && currentSwipe.x > -0.5f && currentSwipe.x < 0.5f)
                {
                    this.OpenPhone();
                }
                
                //swipe down
                if(currentSwipe.y < 0 && currentSwipe.x > -0.5f && currentSwipe.x < 0.5f)
                {
                    this.ClosePhone();
                }
            }
        }
    }
}

