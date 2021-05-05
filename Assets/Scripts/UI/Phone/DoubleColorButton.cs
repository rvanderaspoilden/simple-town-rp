using UnityEngine;
using UnityEngine.UI;

public class DoubleColorButton : ColorButton {
    [Header("Settings")]
    [SerializeField]
    private Image primaryImg;
    
    [SerializeField]
    private Image secondaryImg;

    public override void Setup(PropsPreset preset, PhoneArticleCardUI card) {
        base.Setup(preset, card);
        
        this.primaryImg.color = preset.Primary.Color;
        this.secondaryImg.color = preset.Secondary.Color;
    }
}
