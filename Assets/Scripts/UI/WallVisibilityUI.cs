using System;
using Sim.Enums;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class WallVisibilityUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Sprite wallHiddenSprite;

        [SerializeField]
        private Sprite wallFullSprite;

        private Image image;

        private Button button;

        private ApartmentController bindApartment;

        public static WallVisibilityUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }

            this.image = GetComponent<Image>();
            this.button = GetComponent<Button>();
        }

        private void OnEnable() {
            this.CheckValidity();
        }

        private void Start() {
            this.button.onClick.AddListener(() => {
                if (this.bindApartment) {
                    this.bindApartment.ToggleWallVisibility();
                } else {
                    Debug.LogError("No apartment bind to toggle wall visibility");
                }
            });

            this.CheckValidity();

            ApartmentController.OnWallVisibilityModeChanged += this.UpdateGraphic;
        }

        private void OnDestroy() {
            this.button.onClick.RemoveAllListeners();

            ApartmentController.OnWallVisibilityModeChanged -= this.UpdateGraphic;
        }
        
        public void Bind(ApartmentController apartmentController) {
            this.bindApartment = apartmentController;
        }

        public void Bind(ApartmentController apartmentController, VisibilityModeEnum currentVisibility) {
            this.Bind(apartmentController);
            this.UpdateGraphic(currentVisibility);
        }

        private void CheckValidity() {
            this.button.interactable = !CameraManager.Instance || BuildManager.Instance.GetMode() != BuildModeEnum.WALL_PAINT;
        }

        private void UpdateGraphic(VisibilityModeEnum mode) {
            this.image.sprite = mode == VisibilityModeEnum.FORCE_HIDE ? this.wallHiddenSprite : this.wallFullSprite;
        }
    }
}