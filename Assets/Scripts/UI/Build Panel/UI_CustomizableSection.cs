using System;
using System.Collections.Generic;
using Network.Messages;
using Sim.Scriptables;
using Sim.Utils;
using TMPro;
using UnityEngine;

public class UI_CustomizableSection : MonoBehaviour {
    [SerializeField]
    private TextMeshProUGUI labelTxt;

    [SerializeField]
    private Transform colorContainer;

    [SerializeField]
    private SimpleColorButton colorButtonPrefab;

    private List<SimpleColorButton> _colorButtons = new List<SimpleColorButton>();

    private Action<CustomizedMaterialPart> _onValueChanged;

    private CustomizableMaterialPart _config;

    public void Setup(CustomizableMaterialPart config, Color value, Action<CustomizedMaterialPart> onValueChanged) {
        this._config = config;
        this._onValueChanged = onValueChanged;
        this.labelTxt.text = config.question;

        InstantiateColors();
        
        SetValue(value);
    }

    private void SetValue(Color value) {
        ColorButton match = _colorButtons.Find(x => x.Color == value);

        if (match) {
            SetSelectedColor(match);
        } else {
            Debug.LogError("Value is not found in all available colors");
        }
    }

    private void InstantiateColors() {
        CommonUtils.ClearChildren(this.colorContainer);

        this._colorButtons.Clear();

        foreach (Color color in this._config.availableColors) {
            SimpleColorButton button = Instantiate(this.colorButtonPrefab, this.colorContainer);
            button.Setup(color, () => SetSelectedColor(button));
            this._colorButtons.Add(button);
        }
    }

    private void SetSelectedColor(ColorButton selectedColor) {
        foreach (var colorButton in this._colorButtons) {
            colorButton.SetSelectorActive(selectedColor == colorButton);
        }

        _onValueChanged?.Invoke(new CustomizedMaterialPart() {
            id = this._config.id,
            color = ((SimpleColorButton) selectedColor).Color
        });
    }
}