using System;
using UnityEngine;
using UnityEngine.UI;

public class SimpleColorButton : ColorButton {
    [Header("Settings")]
    [SerializeField]
    private Image primaryImg;

    private Color _color;

    public override void Setup(PropsPreset preset, PhoneArticleCardUI card) {
        base.Setup(preset, card);

        this.primaryImg.color = preset.Primary.Color;
    }

    public override void Setup(Color color, Action onSelect) {
        base.Setup(color, onSelect);

        this._color = color;

        this.primaryImg.color = color;
    }

    public Color Color => _color;
}