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

            // Root = bounds de la forme rotée × cellSize (= zone d'occupation réelle
            // sur la grille). bgImage (stretched) suit le root.
            var rotatedBounds = item.GetRotatedShape(rotation).Bounds();
            Rect.sizeDelta = new Vector2(rotatedBounds.width * cellSize, rotatedBounds.height * cellSize);

            if (iconImage != null) {
                iconImage.sprite = item.Definition.icon;
                iconImage.color = item.Definition.tint;
                iconImage.preserveAspect = false;

                // Icône stretched au root (= zone d'occupation rotée). Rotation
                // visuelle appliquée au sprite via localRotation, mais le rect
                // reste contenu dans le parent → jamais d'overflow.
                var ir = iconImage.rectTransform;
                ir.anchorMin = Vector2.zero;
                ir.anchorMax = Vector2.one;
                ir.pivot = new Vector2(0.5f, 0.5f);
                ir.offsetMin = Vector2.zero;
                ir.offsetMax = Vector2.zero;
                ir.anchoredPosition = Vector2.zero;
                ir.localScale = Vector3.one;
                ir.localRotation = Quaternion.Euler(0f, 0f, -90f * rotation);
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
