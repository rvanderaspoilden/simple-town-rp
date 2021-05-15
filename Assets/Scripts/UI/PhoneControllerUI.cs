using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Sim {
    public class PhoneControllerUI : MonoBehaviour, IPointerDownHandler {
        [Header("Settings")]
        [SerializeField]
        private float openedAnchorY;

        [SerializeField]
        private float openAnimationDuration;

        [SerializeField]
        private float closeAnimationDuration;

        [SerializeField]
        private AudioClip unlockSound;

        [SerializeField]
        private AudioClip lockSound;

        [SerializeField]
        private CanvasGroup lockScreenCanvasGroup;

        [SerializeField]
        private CanvasGroup actionBarCanvasGroup;

        [SerializeField]
        private List<PhoneApplicationButton> applications;

        private Vector2 firstPressPos;
        private Vector2 secondPressPos;
        private Vector2 currentSwipe;

        private bool phoneOpened;

        private float defaultPhoneAnchorPosY;

        private RectTransform rectTransform;

        private PhoneApplicationUI currentActiveApplication;

        public static PhoneControllerUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this);
            } else {
                Instance = this;
                this.rectTransform = GetComponent<RectTransform>();
                this.defaultPhoneAnchorPosY = this.rectTransform.anchoredPosition.y;
                this.firstPressPos = Vector2.negativeInfinity;

                this.lockScreenCanvasGroup.alpha = 1;
                this.actionBarCanvasGroup.alpha = 0;
                this.actionBarCanvasGroup.interactable = false;

                this.applications.ForEach(x => x.Application.gameObject.SetActive(false));
            }
        }

        private void Update() {
            this.Swipe();
        }

        public void OpenApplication(PhoneApplicationButton phoneApplicationButton) {
            phoneApplicationButton.Application.gameObject.SetActive(true);
            this.currentActiveApplication = phoneApplicationButton.Application;
            this.actionBarCanvasGroup.alpha = 1;
            this.actionBarCanvasGroup.interactable = true;
        }

        public void BackToHome() {
            this.currentActiveApplication.gameObject.SetActive(false);
            this.currentActiveApplication = null;
            this.actionBarCanvasGroup.alpha = 0;
            this.actionBarCanvasGroup.interactable = false;
        }

        public void BackAction() {
            if (!this.currentActiveApplication) return;
            
            this.currentActiveApplication.Back();
        }

        public void OnPointerDown(PointerEventData eventData) {
            if (!eventData.pointerCurrentRaycast.gameObject.CompareTag("Swipe Area")) return;

            firstPressPos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
        }

        public void Swipe() {
            if (Input.GetMouseButtonUp(0) && this.firstPressPos != Vector2.negativeInfinity) {
                secondPressPos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

                currentSwipe = new Vector2(secondPressPos.x - firstPressPos.x, secondPressPos.y - firstPressPos.y);

                currentSwipe.Normalize();

                //swipe upwards
                if (currentSwipe.y > 0 && currentSwipe.x > -0.5f && currentSwipe.x < 0.5f) {
                    this.OpenPhone();
                }

                //swipe down
                if (currentSwipe.y < 0 && currentSwipe.x > -0.5f && currentSwipe.x < 0.5f) {
                    this.ClosePhone();
                }

                this.firstPressPos = Vector2.negativeInfinity;
            }
        }

        private void OpenPhone() {
            if (!this.phoneOpened) {
                HUDManager.Instance.PlaySound(this.unlockSound, .5f);
                this.rectTransform.DOAnchorPosY(this.openedAnchorY, this.openAnimationDuration).SetEase(Ease.Flash).OnComplete(() => this.phoneOpened = true);
                this.lockScreenCanvasGroup.DOComplete();
                this.lockScreenCanvasGroup.DOFade(0, .3f);
            }
        }

        private void ClosePhone() {
            if (this.phoneOpened) {
                HUDManager.Instance.PlaySound(this.lockSound, .5f);
                this.rectTransform.DOAnchorPosY(this.defaultPhoneAnchorPosY, this.closeAnimationDuration)
                    .SetEase(Ease.OutBounce)
                    .OnComplete(() => this.phoneOpened = false);
                
                this.lockScreenCanvasGroup.DOComplete();
                this.lockScreenCanvasGroup.DOFade(1, .3f);
            }
        }
    }
}