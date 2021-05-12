using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopCoverDetailUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Image previewImg;

    [SerializeField]
    private TextMeshProUGUI priceTxt;

    private Color selectedColor = Color.white;

    private CoverConfig config;

    public void Setup(CoverConfig coverConfig) {
        this.config = coverConfig;
        this.previewImg.sprite = this.config.Sprite;
        this.priceTxt.text = $"{this.config.Price} / m²";
    }

    public void SetColor(Color color) {
        this.selectedColor = color;
        this.previewImg.color = this.selectedColor;
    }

    public void AddToCart() {
        
    }
}
