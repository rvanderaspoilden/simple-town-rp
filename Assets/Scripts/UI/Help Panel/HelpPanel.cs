using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

public class HelpPanel : MonoBehaviour, IPointerExitHandler {
    [Header("Settings")]
    [SerializeField]
    private GameObject helpContainer;

    [SerializeField]
    private Transform scrollViewContent;


    [SerializeField]
    private CanvasGroup popupCanvasGroup;

    [Header("Only for debug")]
    [SerializeField]
    private HelpConfig currentConfig;

    private bool isOpened;

    private bool isHover;

    public static HelpPanel Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
        } else {
            Instance = this;
            this.UpdatePopupVisibility();
        }
    }

    private void OnEnable() {
        this.CloseView();
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (!isOpened) {
            this.isHover = false;
            this.UpdatePopupVisibility();
        }
    }

    public void Setup(HelpConfig config) {
        this.currentConfig = config;
        this.ClearContent();

        config.OrderedContent.ForEach(x => Instantiate(x, this.scrollViewContent));
    }

    public void Toggle() {
        this.isOpened = !this.isOpened;
        this.UpdateContentVisibility();
    }

    public void CloseView() {
        this.isOpened = false;
        this.UpdateContentVisibility();
    }

    public bool IsHover {
        get => isHover;
        set {
            isHover = value;
            this.UpdatePopupVisibility();
        }
    }

    private void ClearContent() {
        foreach (Transform child in this.scrollViewContent) {
            Destroy(child.gameObject);
        }
    }

    private void UpdateContentVisibility() {
        this.helpContainer.SetActive(this.isOpened);
    }

    private void UpdatePopupVisibility() {
        this.popupCanvasGroup.DOFade(this.isHover ? 1 : 0, .3f);
        this.popupCanvasGroup.blocksRaycasts = this.isHover;
        this.popupCanvasGroup.interactable = this.isHover;
    }
}