using System;
using System.Collections.Generic;
using System.Linq;
using Network.Messages;
using Sim.Scriptables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Build_Panel {
    public class BuildPanelUI : MonoBehaviour {
        [SerializeField]
        private Transform fieldsContainer;

        [SerializeField]
        private UI_CustomizableSection customizableSectionPrefab;

        [SerializeField]
        private UI_TextField textFieldPrefab;

        [SerializeField]
        private Button confirmButton;

        private BuildingConfig _config;

        private Action<CreateBuildingMessage> onCreate;

        private Action OnCancel;

        private string _name;

        private Dictionary<int, CustomizedMaterialPart> _customizedMaterialPartsById = new Dictionary<int, CustomizedMaterialPart>();

        private void OnDisable() {
            this._customizedMaterialPartsById.Clear();
        }

        public void Setup(BuildingConfig config, Action<CreateBuildingMessage> _onCreate, Action _onCancel) {
            this._config = config;
            this.onCreate = _onCreate;
            this.OnCancel = _onCancel;

            CommonUtils.ClearChildren(this.fieldsContainer);

            UI_TextField buildingNameField = Instantiate(this.textFieldPrefab, this.fieldsContainer);
            buildingNameField.Setup("Choisir un nom", (value) => { this._name = value; });

            if (config.IsCustomizable) {
                foreach (CustomizableMaterialPart customizableMaterialPart in config.CustomizableMaterialParts) {
                    this.InstantiateCustomizableSection(customizableMaterialPart);
                }
            }

            this.CheckCreateConstraints();
        }

        private void InstantiateCustomizableSection(CustomizableMaterialPart customizableMaterialPart) {
            UI_CustomizableSection section = Instantiate(this.customizableSectionPrefab, this.fieldsContainer);
            section.Setup(customizableMaterialPart, customizableMaterialPart.availableColors[0], (value) => {
                if (this._customizedMaterialPartsById.ContainsKey(value.id)) {
                    this._customizedMaterialPartsById[value.id] = value;
                } else {
                    this._customizedMaterialPartsById.Add(value.id, value);
                }

                this.CheckCreateConstraints();
            });
        }

        public void Create() {
            this.onCreate?.Invoke(new CreateBuildingMessage() {
                buildingId = this._config.ID,
                customizedMaterialParts = this._customizedMaterialPartsById.Values.ToArray()
            });
        }

        public void Cancel() {
            this.OnCancel?.Invoke();
        }

        private void CheckCreateConstraints() {
            this.confirmButton.interactable = this._customizedMaterialPartsById.Count == this._config.CustomizableMaterialParts.Length;
        }
    }
}