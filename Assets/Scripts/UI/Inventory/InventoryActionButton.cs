using Sim.Interactables;
using TMPro;
using UnityEngine;

public class InventoryActionButton : MonoBehaviour {
    private TextMeshProUGUI _label;
    private Action _action;

    private void Awake() {
        this._label = GetComponentInChildren<TextMeshProUGUI>();
    }

    public void Setup(Action action) {
        this._label.text = action.Label;
        this._action = action;
    }

    public void Execute() {
        this._action.Execute();
    }
}
