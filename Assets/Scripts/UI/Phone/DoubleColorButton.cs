using UnityEngine;
using UnityEngine.UI;

public class DoubleColorButton : ColorButton {
    [Header("Settings")]
    [SerializeField]
    private Image primaryImg;
    
    [SerializeField]
    private Image secondaryImg;
    
    private PropsPreset preset;
    
    public override void Setup(PropsPreset propsPreset) {
        this.preset = propsPreset;
        this.primaryImg.color = propsPreset.Primary.Color;
        this.secondaryImg.color = propsPreset.Secondary.Color;
    }
}
