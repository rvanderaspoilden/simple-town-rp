using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public abstract class ColorButton : MonoBehaviour, IPointerClickHandler {
    [Header("Settings")]
    [SerializeField]
    private Image selectorImg;

    private PhoneArticleCardUI cardUI;
    private PropsPreset propsPreset;

    public virtual void Setup(PropsPreset preset, PhoneArticleCardUI card) {
        this.cardUI = card;
        this.propsPreset = preset;
    }

    public void SetSelector(bool isActive) {
        this.selectorImg.enabled = isActive;
    }

    public void OnPointerClick(PointerEventData eventData) {
        this.cardUI.SelectPreset(this.propsPreset);
    }
}