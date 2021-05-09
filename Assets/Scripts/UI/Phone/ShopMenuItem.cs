using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class ShopMenuItem : MonoBehaviour, IPointerClickHandler {
    [Header("Settings")]
    [SerializeField]
    private TextMeshProUGUI text;

    [SerializeField]
    private TMP_FontAsset activeFontAsset;

    [SerializeField]
    private TMP_FontAsset normalFontAsset;
    
    [SerializeField]
    private Color activeColor;

    [SerializeField]
    private Color normalColor;

    [SerializeField]
    private CanvasGroup canvasGroup;

    private ShopCategoryConfig config;

    private ShopMenuUI shopMenuUI;

    private bool active;

    private void Awake() {
        this.canvasGroup.alpha = 0;
    }
    
    public void Init(ShopCategoryConfig configuration, ShopMenuUI menuUI) {
        this.config = configuration;
        this.shopMenuUI = menuUI;
        this.text.text = this.config.CategoryName;
    }

    public void SetActive(bool value) {
        this.active = value;

        this.text.color = this.active ? this.activeColor : this.normalColor;
        this.text.font = this.active ? this.activeFontAsset : this.normalFontAsset;
        this.text.fontStyle = this.active ? FontStyles.Bold : FontStyles.Normal;
    }

    public CanvasGroup CanvasGroup => canvasGroup;

    public ShopCategoryConfig Config => config;
    
    public void OnPointerClick(PointerEventData eventData) {
        this.shopMenuUI.Select(this);
    }
}