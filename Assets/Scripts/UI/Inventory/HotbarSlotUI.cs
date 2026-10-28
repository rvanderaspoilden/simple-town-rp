using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Vue d'un slot de la barre d'accès rapide. Pur affichage — la sélection et le
/// contenu viennent de <see cref="HotbarUI"/> (sources : snapshot poche + PlayerHands).
/// </summary>
public class HotbarSlotUI : MonoBehaviour, IDropHandler
{
    /// <summary>SlotIndex poche que cette cellule reflète — posé par HotbarUI au clonage.</summary>
    public int SlotIndex { get; set; }

    [SerializeField] private Image background;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text keyLabel;
    [SerializeField, Tooltip("Visuel de sélection (ex. cadre « Selected » du style Synty). " +
        "Si renseigné, SetSelected le toggle au lieu de teinter le fond.")]
    private GameObject selectedVisual;

    [Header("Couleurs")]
    [SerializeField] private Color normalColor   = new Color(1f, 1f, 1f, 0.55f);
    [SerializeField] private Color selectedColor = new Color(0.95f, 0.65f, 0.35f, 1f);

    public void SetKey(string key)
    {
        if (keyLabel) keyLabel.text = key;
    }

    public void SetIcon(Sprite sprite)
    {
        if (!icon) return;
        icon.sprite  = sprite;
        icon.enabled = sprite != null;
    }

    public void SetSelected(bool selected)
    {
        if (selectedVisual != null) { selectedVisual.SetActive(selected); return; }
        if (background) background.color = selected ? selectedColor : normalColor;
    }

    /// <summary>Drop d'un draggable d'inventaire sur la cellule : délégué à HotbarUI,
    /// qui route un move/swap vers le slot de poche correspondant.</summary>
    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem dragged = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<DraggableItem>() : null;
        if (dragged == null) return;
        GetComponentInParent<HotbarUI>()?.HandleDropOnSlot(this, dragged);
    }
}
