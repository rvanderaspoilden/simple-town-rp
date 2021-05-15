using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PhoneCoverCardUI : PhoneCardUI {
    [Header("Settings")]
    [SerializeField]
    private TextMeshProUGUI displayNameTxt;

    [SerializeField]
    private TextMeshProUGUI isCustomTxt;

    [SerializeField]
    private TextMeshProUGUI priceTxt;

    [SerializeField]
    private Image image;

    private CoverConfig config;

    public void Setup(CoverConfig coverConfig) {
        this.config = coverConfig;
        this.displayNameTxt.text = coverConfig.GetDisplayName();
        this.isCustomTxt.text = coverConfig.AllowCustomColor() ? "Customizable" : "Not Customizable";
        this.priceTxt.text = $"{coverConfig.Price} / m²";
        this.image.sprite = coverConfig.Sprite;
    }

    public void Select() {
        ShopUI.Instance.ShowCoverDetails(config);
    }

    public override string GetDisplayName() => this.config.GetDisplayName();
}