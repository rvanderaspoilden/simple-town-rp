using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SubGamePanelUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Button startBtn;

    private CanvasGroup _canvasGroup;

    public static SubGamePanelUI Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
        } else {
            Instance = this;
            this._canvasGroup = GetComponent<CanvasGroup>();
            this.Close();
        }
    }

    public void Open(SubGameConfiguration subGameConfiguration) {
        this._canvasGroup.alpha = 1;
        this._canvasGroup.interactable = true;
        this._canvasGroup.blocksRaycasts = true;

        this.startBtn.onClick.RemoveAllListeners();

        switch (subGameConfiguration.SubGameType) {
            case SubGameType.DREAM:
                this.startBtn.GetComponentInChildren<TextMeshProUGUI>().text = "Rêver";
                this.startBtn.gameObject.SetActive(true);
                this.startBtn.onClick.AddListener(() => {
                    SubGameController.Instance.LaunchSubGame(subGameConfiguration, true);
                    this.startBtn.gameObject.SetActive(false);
                });
                break;
        }
    }

    public void Close() {
        this._canvasGroup.alpha = 0;
        this._canvasGroup.interactable = false;
        this._canvasGroup.blocksRaycasts = false;
    }
}