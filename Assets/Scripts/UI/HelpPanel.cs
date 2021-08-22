using System;
using UnityEngine;

public class HelpPanel : MonoBehaviour {

    [Header("Settings")]
    [SerializeField]
    private GameObject helpContainer;

    [SerializeField]
    private Transform scrollViewContent;

    [Header("Only for debug")]
    [SerializeField]
    private HelpConfig currentConfig;
    
    private bool isOpened;
    
    public static HelpPanel Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
        } else {
            Instance = this;
        }
    }

    private void OnEnable() {
        this.CloseView();
    }

    public void Setup(HelpConfig config) {
        this.currentConfig = config;
        this.ClearContent();
        
        config.OrderedContent.ForEach(x => Instantiate(x, this.scrollViewContent));
    }

    public void Toggle() {
        this.isOpened = !this.isOpened;
        this.UpdateVisibility();
    }

    public void CloseView() {
        this.isOpened = false;
        this.UpdateVisibility();
    }

    private void ClearContent() {
        foreach (Transform child in this.scrollViewContent) {
            Destroy(child.gameObject);
        }
    }

    private void UpdateVisibility() {
        this.helpContainer.SetActive(this.isOpened);
    }
}
