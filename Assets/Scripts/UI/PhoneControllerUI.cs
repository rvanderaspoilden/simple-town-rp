using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

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
        private ShopViewUI shopViewUI;

        [SerializeField]
        private HomeViewUI homeViewUI;

        [SerializeField]
        private List<PhoneApplication> applications;

        private Vector2 firstPressPos;
        private Vector2 secondPressPos;
        private Vector2 currentSwipe;

        private bool phoneOpened;

        private float defaultPhoneAnchorPosY;

        private RectTransform rectTransform;

        private PhoneApplication currentActiveApplication;

        public static PhoneControllerUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this);
            } else {
                Instance = this;
                this.rectTransform = GetComponent<RectTransform>();
                this.defaultPhoneAnchorPosY = this.rectTransform.anchoredPosition.y;
                this.firstPressPos = Vector2.negativeInfinity;

                this.applications.ForEach(x => x.Application.SetActive(false));
            }
        }

        private void Update() {
            this.Swipe();
        }

        public void OpenApplication(PhoneApplication phoneApplication) {
            phoneApplication.Application.SetActive(true);
            this.currentActiveApplication = phoneApplication;
        }

        public void BackToHome() {
            this.currentActiveApplication.Application.SetActive(false);
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
            }
        }

        private void ClosePhone() {
            if (this.phoneOpened) {
                HUDManager.Instance.PlaySound(this.unlockSound, .1f);
                this.rectTransform.DOAnchorPosY(this.defaultPhoneAnchorPosY, this.closeAnimationDuration).SetEase(Ease.OutBounce)
                    .OnComplete(() => this.phoneOpened = false);
            }
        }
    }
}