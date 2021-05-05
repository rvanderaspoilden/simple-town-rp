using UnityEngine;
using UnityEngine.UI;

public class SimpleColorButton : ColorButton {
    [Header("Settings")]
    [SerializeField]
    private Image primaryImg;

    public override void Setup(PropsPreset preset, PhoneArticleCardUI card) {
        base.Setup(preset, card);
        
        this.primaryImg.color = preset.Primary.Color;
    }
}
