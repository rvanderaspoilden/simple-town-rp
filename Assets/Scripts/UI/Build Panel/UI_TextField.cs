using System;
using TMPro;
using UnityEngine;

public class UI_TextField : MonoBehaviour {
    [SerializeField]
    private TextMeshProUGUI labelTxt;

    [SerializeField]
    private TMP_InputField inputField;

    private Action<string> _onValueChanged;

    public void Setup(string label, Action<string> onValueChange, string value = null) {
        this.labelTxt.text = label;
        this._onValueChanged = onValueChange;

        if (!string.IsNullOrEmpty(value)) this.inputField.text = value;
    }

    public void OnInputFieldChange(string value) {
        this._onValueChanged?.Invoke(value);
    }
}