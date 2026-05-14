using UnityEngine;
using UnityEngine.UI;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Représentation visuelle d'un PackageItemInstance, soit dans la grille
    /// soit dans le tray. Pose les cellules sous forme d'Image enfants.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PackageItemView : MonoBehaviour {
        [SerializeField] private Image iconImage;
        [SerializeField] private CanvasGroup canvasGroup;

        private RectTransform _rect;
        public RectTransform Rect => _rect != null ? _rect : (_rect = GetComponent<RectTransform>());

        public PackageItemInstance Instance { get; private set; }

        public void Bind(PackageItemInstance instance, float cellSize) {
            Instance = instance;
            if (iconImage != null) {
                iconImage.sprite = instance.Definition.icon;
                iconImage.color = instance.Definition.tint;
            }
            ResizeToShape(instance.GetRotatedShape(0), cellSize);
        }

        public void SetRotationVisual(int rotation, float cellSize) {
            ResizeToShape(Instance.GetRotatedShape(rotation), cellSize);
            // Visual angle for the icon (the shape itself is already pre-rotated
            // in cells, so we only rotate the sprite for visual coherence).
            if (iconImage != null) {
                iconImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f * rotation);
            }
        }

        private void ResizeToShape(PackageShape shape, float cellSize) {
            var bounds = shape.Bounds();
            Rect.sizeDelta = new Vector2(bounds.width * cellSize, bounds.height * cellSize);
        }

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
