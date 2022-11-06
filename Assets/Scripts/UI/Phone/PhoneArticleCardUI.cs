using System;
using System.Collections.Generic;
using System.Linq;
using Sim;
using Sim.Entities;
using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PhoneArticleCardUI : PhoneCardUI {
    [Header("Settings")]
    [SerializeField]
    private TextMeshProUGUI nameTxt;

    [SerializeField]
    private TextMeshProUGUI categoryTxt;

    [SerializeField]
    private TextMeshProUGUI priceTxt;

    [SerializeField]
    private Transform colorContainer;

    [SerializeField]
    private Image previewImg;

    [SerializeField]
    private SimpleColorButton simpleColorButtonPrefab;

    [SerializeField]
    private DoubleColorButton doubleColorButtonPrefab;

    [SerializeField]
    private TripleColorButton tripleColorButtonPrefab;

    private PropsPreset selectedPreset;

    private PropsConfig propsConfig;

    private Dictionary<int, ColorButton> colorButtonOfPresetId;

    public void Setup(PropsConfig config) {
        this.propsConfig = config;
        this.nameTxt.text = config.GetDisplayName();
        this.categoryTxt.text = DatabaseManager.GetShopCategoryByPropsType(config.GetPropsType()).CategoryName;
        this.priceTxt.text = config.Price.ToString();

        if (config.Sprite) {
            this.previewImg.sprite = config.Sprite;
        }

        if (config.Presets?.Length > 0) {
            this.colorButtonOfPresetId = config.Presets.ToDictionary(preset => preset.ID, preset => {
                ColorButton prefabToUse = this.simpleColorButtonPrefab;

                if (preset.Tertiary.Enabled) {
                    prefabToUse = this.tripleColorButtonPrefab;
                } else if (preset.Secondary.Enabled) {
                    prefabToUse = this.doubleColorButtonPrefab;
                }

                ColorButton colorButton = Instantiate(prefabToUse, this.colorContainer);
                colorButton.Setup(preset, this);

                return colorButton;
            });

            this.SelectPreset(config.Presets[0]);
        }
    }

    public PropsConfig PropsConfig => propsConfig;

    public void SelectPreset(PropsPreset preset) {
        this.selectedPreset = preset;

        foreach (var keyValuePair in this.colorButtonOfPresetId) {
            keyValuePair.Value.SetSelectorActive(keyValuePair.Key == this.selectedPreset.ID);
        }
    }

    public void AddToCart() {
        CreateDeliveryRequest request = new CreateDeliveryRequest {
            type = DeliveryType.PROPS,
            recipientId = PlayerController.Local.CharacterData.Id,
            propsConfigId = propsConfig.GetId(),
        };

        if (this.selectedPreset != null) {
            request.propsPresetId = selectedPreset.ID;
        }

        PlayerController.Local.connectionToServer.Send(request);
    }

    public override string GetDisplayName() => this.propsConfig.GetDisplayName();
}