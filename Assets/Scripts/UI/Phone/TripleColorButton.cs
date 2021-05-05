using UnityEngine;
using UnityEngine.UI;

public class TripleColorButton : ColorButton {
    [Header("Settings")]
    [SerializeField]
    private Image primaryImg;
    
    [SerializeField]
    private Image secondaryImg;
    
    [SerializeField]
    private Image tertiaryImg;

    public override void Setup(PropsPreset preset, PhoneArticleCardUI card) {
        base.Setup(preset, card);
        
        this.primaryImg.color = preset.Primary.Color;
        this.secondaryImg.color = preset.Secondary.Color;
        this.tertiaryImg.color = preset.Tertiary.Color;
    }
}
