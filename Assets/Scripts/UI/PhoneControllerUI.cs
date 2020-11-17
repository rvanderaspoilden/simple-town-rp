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

        private Vector2 firstPressPos;
        private Vector2 secondPressPos;
        private Vector2 currentSwipe;
        
        private bool phoneOpened;
        
        private float defaultPhoneAnchorPosY;

        private RectTransform rectTransform;
        
        private void Awake() {
            this.rectTransform = GetComponent<RectTransform>();
            this.defaultPhoneAnchorPosY = this.rectTransform.anchoredPosition.y;
            this.firstPressPos = Vector2.negativeInfinity;
        }

        private void Update() {
            this.Swipe();
        }

        public void OnPointerDown(PointerEventData eventData) {
            firstPressPos = new Vector2(Input.mousePosition.x,Input.mousePosition.y);
        }
        
        public void Swipe()
        {
            if(Input.GetMouseButtonUp(0) && this.firstPressPos != Vector2.negativeInfinity)
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

                this.firstPressPos = Vector2.negativeInfinity;
            }
        }
        
        private void OpenPhone() {
            if (!this.phoneOpened) {
                this.rectTransform.DOAnchorPosY(this.openedAnchorY, this.openAnimationDuration).SetEase(Ease.Flash).OnComplete(() => this.phoneOpened = true);
            }
        }

        private void ClosePhone() {
            if (this.phoneOpened) {
                this.rectTransform.DOAnchorPosY(this.defaultPhoneAnchorPosY, this.closeAnimationDuration).SetEase(Ease.OutBounce).OnComplete(() => this.phoneOpened = false);
            }
        }
    }
}