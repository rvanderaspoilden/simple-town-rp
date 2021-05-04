using UnityEngine;
using UnityEngine.UI;

public class SimpleColorButton : ColorButton {
    [Header("Settings")]
    [SerializeField]
    private Image primaryImg;

    private PropsPreset preset;
    
    public override void Setup(PropsPreset propsPreset) {
        this.preset = propsPreset;
        this.primaryImg.color = propsPreset.Primary.Color;
    }
}
