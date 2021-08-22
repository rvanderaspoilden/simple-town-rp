using Sim.Building;
using Sim.Enums;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    public class BuildPreviewPanelUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Button validationBtn;

        [SerializeField]
        private WallVisibilityUI wallVisibilityUI;

        [SerializeField]
        private Image currentModeImg;

        [SerializeField]
        private Sprite propsEditSprite;

        [SerializeField]
        private Sprite paintEditSprite;

        [SerializeField]
        private HelpConfig helpConfig;

        public delegate void OnValidateEvent();

        public static event OnValidateEvent OnValidate;

        public delegate void OnCanceledEvent();

        public static event OnCanceledEvent OnCanceled;

        private void OnEnable() {
            BuildPreview.OnPlaceableStateChanged += this.SetValidateButtonInteractable;

            HelpPanel.Instance.Setup(this.helpConfig);

            this.currentModeImg.sprite = BuildManager.Instance.GetMode() == BuildModeEnum.WALL_PAINT || BuildManager.Instance.GetMode() == BuildModeEnum.GROUND_PAINT
                ? this.paintEditSprite
                : propsEditSprite;

            this.SetValidateButtonInteractable(true);
        }

        private void OnDisable() {
            BuildPreview.OnPlaceableStateChanged -= this.SetValidateButtonInteractable;
        }

        public void Validate() {
            OnValidate?.Invoke();
        }

        public void Cancel() {
            OnCanceled?.Invoke();
        }

        private void SetValidateButtonInteractable(bool state) {
            this.validationBtn.interactable = state;
        }
    }
}