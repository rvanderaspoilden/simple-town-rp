using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim.Constellation {
    // Petit helper accroché au bouton Débloquer interne d'une carte. Donne au bouton son
    // propre feedback visuel (léger scale) au survol — UNIQUEMENT quand le bouton est
    // interactable (= label « Débloquer »). Aucun feedback (visuel ou sonore) quand le
    // bouton montre « X pts » ou « Verrouillé ». Le son de survol vit sur le nœud.
    [RequireComponent(typeof(RectTransform))]
    public class ConstellationUnlockButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] private float hoverScale = 1.06f;
        [SerializeField] private float tweenDuration = 0.12f;

        private RectTransform _rt;
        private Button _button;
        private Vector3 _baseScale = Vector3.one;

        private void Awake() {
            _rt = (RectTransform)transform;
            _button = GetComponent<Button>();
            _baseScale = _rt.localScale;
        }

        private void OnEnable() {
            if (_rt != null) _rt.localScale = _baseScale;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (_button == null || !_button.interactable) return;
            _rt.DOKill();
            _rt.DOScale(_baseScale * hoverScale, tweenDuration).SetEase(Ease.OutBack);
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (_button == null) return;
            _rt.DOKill();
            _rt.DOScale(_baseScale, tweenDuration).SetEase(Ease.OutCubic);
        }
    }
}
