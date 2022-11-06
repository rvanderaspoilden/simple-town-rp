using System;
using Network.Messages;
using Sim;
using Sim.Scriptables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Build_Panel {
    public class BuildPanelUI : MonoBehaviour {
        [SerializeField]
        private Transform mainColorContainer;

        [SerializeField]
        private Transform firstStoreColorContainer;

        [SerializeField]
        private Transform secondStoreColorContainer;

        [SerializeField]
        private Transform barColorContainer;

        [SerializeField]
        private SimpleColorButton colorButtonPrefab;

        [SerializeField]
        private Button confirmButton;

        private BuildingConfig _config;

        private Action<CreateBuildingMessage> onCreate;

        private Action OnCancel;

        public void Setup(BuildingConfig config, Action<CreateBuildingMessage> _onCreate, Action _onCancel) {
            this._config = config;
            this.onCreate = _onCreate;
            this.OnCancel = _onCancel;

            if (config.GetType() == typeof(B_FoodContainerConfig)) {
                this.InstantiateColors(((B_FoodContainerConfig) this._config).MainColors, this.mainColorContainer);
                this.InstantiateColors(((B_FoodContainerConfig) this._config).BarColors, this.barColorContainer);
                this.InstantiateColors(((B_FoodContainerConfig) this._config).StoreColors, this.firstStoreColorContainer);
                this.InstantiateColors(((B_FoodContainerConfig) this._config).StoreColors, this.secondStoreColorContainer);
            }
            
            this.CheckCreateConstraints();
        }

        public void Create() { this.OnCancel?.Invoke(); }

        public void Cancel() { this.onCreate?.Invoke(new CreateBuildingMessage(){}); }

        private void InstantiateColors(Color32[] colors, Transform container) {
            CommonUtils.ClearChildren(container);

            foreach (Color color in colors) {
                ColorButton button = Instantiate(this.colorButtonPrefab, container);
                button.Setup(color, () => SetSelectedColor(button, container));
            }
        }

        private void SetSelectedColor(ColorButton selectedColor, Transform container) {
            foreach (Transform child in container) {
                ColorButton button = child.GetComponent<ColorButton>();
                button.SetSelectorActive(selectedColor == button);
            }
            
            this.CheckCreateConstraints();
        }

        private void CheckCreateConstraints() {
            this.confirmButton.interactable = true;
        }
    }
}