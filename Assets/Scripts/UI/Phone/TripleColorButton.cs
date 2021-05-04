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

    private PropsPreset preset;
    
    public override void Setup(PropsPreset propsPreset) {
        this.preset = propsPreset;
        this.primaryImg.color = propsPreset.Primary.Color;
        this.secondaryImg.color = propsPreset.Secondary.Color;
        this.tertiaryImg.color = propsPreset.Tertiary.Color;
    }
}
