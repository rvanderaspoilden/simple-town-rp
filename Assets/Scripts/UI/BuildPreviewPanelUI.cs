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

        [Header("Paint mode")]
        [SerializeField]
        [Tooltip("Optional 'Tout repeindre' button — only shown in paint mode.")]
        private GameObject paintAllBtn;

        public delegate void OnValidateEvent();

        public static event OnValidateEvent OnValidate;

        public delegate void OnCanceledEvent();

        public static event OnCanceledEvent OnCanceled;

        private void OnEnable() {
            BuildPreview.OnPlaceableStateChanged += this.SetValidateButtonInteractable;

            HelpPanel.Instance.Setup(this.helpConfig);

            bool isPaint = BuildManager.Instance.GetMode() == BuildModeEnum.WALL_PAINT
                        || BuildManager.Instance.GetMode() == BuildModeEnum.GROUND_PAINT;

            this.currentModeImg.sprite = isPaint ? this.paintEditSprite : propsEditSprite;

            if (this.paintAllBtn != null) this.paintAllBtn.SetActive(isPaint);

            this.SetValidateButtonInteractable(true);
        }

        /// <summary>Bind this to the "Tout repeindre" button OnClick.</summary>
        public void PaintAll() {
            BuildManager.Instance.PaintAll();
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