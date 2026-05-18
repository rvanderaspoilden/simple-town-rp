using UnityEngine;
using UnityEngine.UI;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Représentation visuelle d'un PackageItemInstance. Deux modes :
    ///   - Tray (slot 1x1 fixe, peu importe la forme)
    ///   - Grid (forme × cellSize, avec rotation visuelle)
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PackageItemView : MonoBehaviour {
        [SerializeField] private Image bgImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private GameObject heavyBadge;
        [SerializeField] private GameObject fragileBadge;

        private RectTransform _rect;
        public RectTransform Rect => _rect != null ? _rect : (_rect = GetComponent<RectTransform>());

        public PackageItemInstance Instance { get; private set; }

        /// <summary>
        /// Lie la définition de l'item à la vue. N'applique pas de layout —
        /// appelle SetTrayLayout / SetGridLayout selon le contexte.
        /// </summary>
        public void Bind(PackageItemInstance instance) {
            Instance = instance;
            if (iconImage != null) {
                iconImage.sprite = instance.Definition.icon;
                iconImage.color = instance.Definition.tint;
                iconImage.preserveAspect = true;
            }
            if (bgImage != null) {
                bgImage.color = instance.Definition.bgColor;
            }
            if (heavyBadge != null) heavyBadge.SetActive(instance.Definition.heavy);
            if (fragileBadge != null) fragileBadge.SetActive(instance.Definition.fragile);
        }

        /// <summary>
        /// Mode tray : slot carré fixe (1×1 logique). L'icône s'étire au
        /// parent avec Preserve Aspect pour garder la silhouette.
        /// </summary>
        public void SetTrayLayout(float slotSize) {
            Rect.sizeDelta = new Vector2(slotSize, slotSize);
            if (iconImage != null) {
                var ir = iconImage.rectTransform;
                ir.anchorMin = Vector2.zero;
                ir.anchorMax = Vector2.one;
                ir.pivot = new Vector2(0.5f, 0.5f);
                ir.offsetMin = Vector2.zero;
                ir.offsetMax = Vector2.zero;
                ir.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// Mode grille : taille = bounds de la forme rotée × cellSize.
        /// L'icône garde sa taille de base (rotation 0) au centre, et est
        /// pivotée visuellement. Ainsi un objet 1×2 reste à l'échelle quand
        /// on le couche en 2×1 — le sprite n'est jamais étiré/écrasé.
        /// </summary>
        public void SetGridLayout(int rotation, float cellSize) {
            var rotatedBounds = Instance.GetRotatedShape(rotation).Bounds();
            Rect.sizeDelta = new Vector2(rotatedBounds.width * cellSize, rotatedBounds.height * cellSize);

            if (iconImage != null) {
                // Icône stretched au root → jamais d'overflow visuel ou de raw rect
                // qui dépasse. La rotation visuelle est appliquée au sprite (pivot
                // centre), donc la silhouette tourne dans le même rect.
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

        // Alias rétro-compatible avec PackageInputController.
        public void SetRotationVisual(int rotation, float cellSize) => SetGridLayout(rotation, cellSize);

        public void SetAlpha(float a) {
            if (canvasGroup != null) canvasGroup.alpha = a;
        }

        public void SetInteractable(bool interactable) {
            if (canvasGroup != null) {
                canvasGroup.interactable = interactable;
                canvasGroup.blocksRaycasts = interactable;
            }
        }
    }
}
