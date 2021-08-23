using Sim.Enums;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class PropsVisibilityUI : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField]
        private Sprite propsHiddenSprite;

        [SerializeField]
        private Sprite propsFullSprite;

        private Image image;

        private Button button;

        private ApartmentController bindApartment;

        public static PropsVisibilityUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
            
            this.image = GetComponent<Image>();
            this.button = GetComponent<Button>();
        }

        private void Start() {
            this.button.onClick.AddListener(() => {
                if (this.bindApartment) {
                    this.bindApartment.TogglePropsVisible();
                } else {
                    Debug.LogError("No apartment binded to toggle props visiblity");
                }
            });
            
            ApartmentController.OnPropsVisibilityModeChanged += this.UpdateGraphic;
        }

        public void Bind(ApartmentController apartmentController) {
            this.bindApartment = apartmentController;
        }

        private void OnDestroy() {
            this.button.onClick.RemoveAllListeners();
            
            ApartmentController.OnPropsVisibilityModeChanged -= this.UpdateGraphic;
        }

        private void UpdateGraphic(VisibilityModeEnum mode) {
            this.image.sprite = mode == VisibilityModeEnum.FORCE_HIDE ? this.propsHiddenSprite : this.propsFullSprite;
        }
    }

}
