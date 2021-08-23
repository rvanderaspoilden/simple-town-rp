using System;
using System.Collections;
using System.Collections.Generic;
using Sim;
using Sim.Enums;
using UnityEngine;
using UnityEngine.UI;

public class MagnetismButton : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Sprite magnetismActivatedSprite;

    [SerializeField]
    private Sprite magnestismDeactivatedSprite;

    private Image image;

    private Button button;

    public static MagnetismButton Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
        } else {
            Instance = this;
        }

        this.image = GetComponent<Image>();
        this.button = GetComponent<Button>();
    }

    private void Start() {
        this.button.onClick.AddListener(() => {
            BuildManager.Instance.ToggleMagnetismState();
            this.UpdateGraphic();
        });

        BuildManager.OnMagnetismStateChange += UpdateGraphic;
    }

    private void OnEnable() {
        this.UpdateGraphic();
    }

    private void OnDestroy() {
        this.button.onClick.RemoveAllListeners();
        
        BuildManager.OnMagnetismStateChange -= UpdateGraphic;
    }

    private void UpdateGraphic() {
        this.image.sprite = BuildManager.Instance.MagnetismActivated || BuildManager.Instance.InstantMagnetismActivated ? this.magnetismActivatedSprite : this.magnestismDeactivatedSprite;
    }
}