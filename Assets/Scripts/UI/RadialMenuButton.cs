using DG.Tweening;
using Sim.Interactables;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim.UI {
    public class RadialMenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
        [Header("Settings")]
        [SerializeField]
        private Image selectorImage;

        [SerializeField]
        private float hoverScaleMultiplier = 1.5f;

        [SerializeField]
        private Color defaultColor = new Color32(77, 77, 77, 255);

        [SerializeField]
        private Color hoverColor = Color.white;

        private RectTransform rectTransform;

        private Action action;

        public delegate void InteractionEvent(Action action);

        public static event InteractionEvent OnClicked;
        
        public static event InteractionEvent OnHover;
        
        public static event InteractionEvent OnExit;

        private void Awake() {
            this.rectTransform = GetComponent<RectTransform>();
        }

        public void Setup(Action actionToHold) {
            this.action = actionToHold;
        }

        public RectTransform RectTransform => rectTransform;

        public void OnPointerEnter(PointerEventData eventData) {
            this.rectTransform.DOComplete();
            this.rectTransform.DOScale(Vector3.one * this.hoverScaleMultiplier, .3f).SetEase(Ease.OutQuad);

            this.selectorImage.DOComplete();
            this.selectorImage.DOColor(hoverColor, .3f).SetEase(Ease.OutQuad);
            
            OnHover?.Invoke(this.action);
        }

        public void OnPointerExit(PointerEventData eventData) {
            this.rectTransform.DOComplete();
            this.rectTransform.DOScale(Vector3.one, .3f).SetEase(Ease.OutQuad);

            this.selectorImage.DOComplete();
            this.selectorImage.DOColor(defaultColor, .3f).SetEase(Ease.OutQuad);
            
            OnExit?.Invoke(this.action);
        }

        public void OnPointerClick(PointerEventData eventData) {
            OnClicked?.Invoke(this.action);
        }
    }
}