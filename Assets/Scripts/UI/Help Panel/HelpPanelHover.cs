using UnityEngine;
using UnityEngine.EventSystems;

public class HelpPanelHover : MonoBehaviour, IPointerEnterHandler {
    public void OnPointerEnter(PointerEventData eventData) {
        HelpPanel.Instance.IsHover = true;
    }
}