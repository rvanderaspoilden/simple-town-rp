using Sim.Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventoryActionButton : MonoBehaviour {
    [SerializeField]
    private TextMeshProUGUI _label;

    [Tooltip("Image affichant l'icône de l'action (enfant « Icon »). Masquée si l'action n'a pas d'icône.")]
    [SerializeField]
    private Image _icon;

    private Action _action;

    private void Awake() {
        if (this._label == null) this._label = GetComponentInChildren<TextMeshProUGUI>();
    }

    public void Setup(Action action) {
        this._label.text = action.Label;
        this._action = action;

        if (this._icon != null) {
            this._icon.sprite = action.Icon;
            this._icon.enabled = action.Icon != null;
        }
    }

    public void Execute() {
        this._action.Execute();
        GetComponentInParent<InventoryActionMenu>()?.Hide();
    }
}
