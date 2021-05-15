using Sim;
using Sim.Entities;
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

    [SerializeField]
    private ColorPaletteController colorPicker;

    private Color selectedColor;

    private CoverConfig config;

    public void Setup(CoverConfig coverConfig) {
        this.config = coverConfig;
        this.previewImg.sprite = this.config.Sprite;
        this.priceTxt.text = $"{this.config.Price} / m²";
        this.colorPicker.gameObject.SetActive(coverConfig.AllowCustomColor());

        if (!coverConfig.AllowCustomColor()) {
            this.SetColor(Color.white);
        }
    }

    public void SetColor(Color color) {
        this.selectedColor = color;
        this.previewImg.color = this.selectedColor;
    }

    public void AddToCart() {
        CreateDeliveryRequest request = new CreateDeliveryRequest {
            type = DeliveryType.COVER,
            recipientId = PlayerController.Local.CharacterData.Id,
            paintConfigId = config.GetId(),
            propsConfigId = config.GetBucketPropsConfig().GetId(),
            color = new[] {this.selectedColor.r, this.selectedColor.g, this.selectedColor.b}
        };

        PlayerController.Local.connectionToServer.Send(request);
    }
}