using Sim;
using TMPro;
using UnityEngine;

public class ShopUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private AudioClip buySuccessSound;

    [SerializeField]
    private TMP_InputField searchFilterInputField;

    [SerializeField]
    private TextMeshProUGUI breadCrumbText;

    [SerializeField]
    private ShopListView listView;
    
    public static ShopUI Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this);
        } else {
            Instance = this;
        }
    }
    
    public void OnBuyResponse(bool isSuccess) {
        if (isSuccess) {
            HUDManager.Instance.PlaySound(this.buySuccessSound, .5f);
            PhoneNotificationService.Instance.DisplayBuySuccessNotification();
        }
    }

    public void ChangeCategory(ShopCategoryConfig config) {
        this.breadCrumbText.text = config.CategoryName;

        this.searchFilterInputField.text = string.Empty;
        
        this.listView.Setup(config);
    }

    public void SearchFilter(string value) {
        this.listView.Filter(value);
    }
}