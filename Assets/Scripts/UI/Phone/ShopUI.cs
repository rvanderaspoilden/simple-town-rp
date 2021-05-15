using Sim;
using Sim.Scriptables;
using TMPro;
using UnityEngine;

public class ShopUI : PhoneApplicationUI {
    [Header("Settings")]
    [SerializeField]
    private AudioClip buySuccessSound;

    [SerializeField]
    private TMP_InputField searchFilterInputField;

    [SerializeField]
    private TextMeshProUGUI breadCrumbText;

    [SerializeField]
    private ShopListView listView;

    [SerializeField]
    private ShopCoverDetailUI coverDetail;

    [SerializeField]
    private ShopMenuUI menuUI;

    private ShopCategoryConfig currentCategoryConfig;

    private CoverConfig currentCoverConfig;

    public static ShopUI Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this);
        } else {
            Instance = this;
            HideCoverDetails();
        }
    }

    public void OnBuyResponse(bool isSuccess) {
        if (isSuccess) {
            HUDManager.Instance.PlaySound(this.buySuccessSound, .5f);
            PhoneNotificationService.Instance.DisplayBuySuccessNotification();
        }
    }

    public void ChangeCategory(ShopCategoryConfig config) {
        this.currentCategoryConfig = config;

        this.searchFilterInputField.text = string.Empty;

        this.SetupListView(config);

        if (this.currentCoverConfig) {
            this.HideCoverDetails();
        }

        this.UpdateBreadcrumb();

        this.menuUI.Close();
    }

    private void SetupListView(ShopCategoryConfig config) {
        this.listView.Setup(config);
    }

    public void ShowCoverDetails(CoverConfig config) {
        this.currentCoverConfig = config;
        this.coverDetail.Setup(config);
        this.coverDetail.gameObject.SetActive(true);
        this.listView.Hide();
        this.UpdateBreadcrumb();
    }

    private void HideCoverDetails() {
        this.currentCoverConfig = null;
        this.coverDetail.gameObject.SetActive(false);
        this.UpdateBreadcrumb();
    }

    public void SearchFilter(string value) {
        if (this.currentCoverConfig) {
            this.HideCoverDetails();
        }

        this.listView.Filter(value);
    }

    private void UpdateBreadcrumb() {
        string text = string.Empty;

        if (this.currentCategoryConfig) {
            text = this.currentCategoryConfig.CategoryName;
        }

        if (this.currentCoverConfig) {
            text += $" > {this.currentCoverConfig.GetDisplayName()}";
        }

        this.breadCrumbText.text = text;
    }

    public override void Back() {
        if (currentCoverConfig) {
            this.listView.Show();
            this.HideCoverDetails();
        } else if (this.menuUI.Active) {
            this.menuUI.Close();
        }
    }
}