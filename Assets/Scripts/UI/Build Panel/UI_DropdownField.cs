using System;
using System.Linq;
using TMPro;
using UnityEngine;

public class UI_DropdownField : MonoBehaviour {
    [SerializeField]
    private TextMeshProUGUI labelTxt;

    [SerializeField]
    private TMP_Dropdown dropdownField;

    private Action<string> _onValueChanged;

    public void Setup(string label, string[] values, string value = null, Action<string> onValueChange = null) {
        this.labelTxt.text = label;
        this._onValueChanged = onValueChange;

        this.dropdownField.ClearOptions();
        this.dropdownField.AddOptions(values.ToList());

        if (!string.IsNullOrEmpty(value)) this.dropdownField.value = this.dropdownField.options.FindIndex(x => x.text == value);
    }

    public void OnDropdownFieldChange(int value) {
        this._onValueChanged?.Invoke(this.dropdownField.options[value].text);
    }
}