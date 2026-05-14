using UnityEngine;
using UnityEngine.UI;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Aperçu translucide de l'objet en cours de drag, snappé sur la grille.
    /// Couleur verte si placement valide, rouge pastel sinon.
    /// </summary>
    public class PackageGhostView : MonoBehaviour {
        [SerializeField] private Image bgImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private Color validColor   = new Color(0.6f, 1f, 0.6f, 0.55f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.6f, 0.6f, 0.45f);

        private RectTransform _rect;
        public RectTransform Rect => _rect != null ? _rect : (_rect = (RectTransform)transform);

        public void Show(PackageItemInstance item, int rotation, float cellSize) {
            gameObject.SetActive(true);
            var bounds = item.GetRotatedShape(rotation).Bounds();
            Rect.sizeDelta = new Vector2(bounds.width * cellSize, bounds.height * cellSize);
            if (iconImage != null) {
                iconImage.sprite = item.Definition.icon;
                iconImage.color = item.Definition.tint;
                iconImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f * rotation);
            }
        }

        public void UpdatePosition(Vector2 anchored, bool valid) {
            Rect.anchoredPosition = anchored;
            if (bgImage != null) bgImage.color = valid ? validColor : invalidColor;
        }

        public void Hide() {
            gameObject.SetActive(false);
        }
    }
}
