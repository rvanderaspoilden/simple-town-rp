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
        private Image forbiddenImage;

        [SerializeField]
        private float hoverScaleMultiplier = 1.5f;

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
            // Affichages « sélecteur » et « interdit » retirés : on les masque ; seul
            // l'effet de scale au survol est conservé.
            if (this.selectorImage  != null) this.selectorImage.gameObject.SetActive(false);
            if (this.forbiddenImage != null) this.forbiddenImage.gameObject.SetActive(false);
        }

        public RectTransform RectTransform => rectTransform;

        // Les boutons restent TOUJOURS interactifs (plus de gate IsForbidden) : l'action
        // s'exécute, et un toast d'erreur s'affiche si elle ne peut pas aboutir.
        public void OnPointerEnter(PointerEventData eventData) {
            this.rectTransform.DOComplete();
            this.rectTransform.DOScale(Vector3.one * this.hoverScaleMultiplier, .3f).SetEase(Ease.OutQuad);

            OnHover?.Invoke(this.action);
        }

        public void OnPointerExit(PointerEventData eventData) {
            this.rectTransform.DOComplete();
            this.rectTransform.DOScale(Vector3.one, .3f).SetEase(Ease.OutQuad);

            OnExit?.Invoke(this.action);
        }

        public void OnPointerClick(PointerEventData eventData) {
            OnClicked?.Invoke(this.action);
        }
    }
}
