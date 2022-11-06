using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public abstract class ColorButton : MonoBehaviour, IPointerClickHandler {
    [Header("Settings")]
    [SerializeField]
    private Image selectorImg;

    private PhoneArticleCardUI cardUI;
    private PropsPreset propsPreset;
    private Action onSelectAction;

    public virtual void Setup(PropsPreset preset, PhoneArticleCardUI card) {
        this.cardUI = card;
        this.propsPreset = preset;
    }

    public virtual void Setup(Color color, Action onSelect) {
        this.onSelectAction = onSelect;
    }


    public void SetSelectorActive(bool isActive) {
        this.selectorImg.enabled = isActive;
    }

    public void OnPointerClick(PointerEventData eventData) {
        this.onSelectAction?.Invoke();

        if (this.cardUI) {
            this.cardUI.SelectPreset(this.propsPreset);
        }
    }
}