using Sim;
using UnityEngine;
using UnityEngine.UI;

public class GridModeButton : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Sprite gridActivatedSprite;

    [SerializeField]
    private Sprite gridDeactivatedSprite;

    private Image image;

    private Button button;

    public static GridModeButton Instance;

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
            BuildManager.Instance.ToggleGridMode();
            this.UpdateGraphic();
        });

        BuildManager.OnGridModeStateChange += UpdateGraphic;
    }

    private void OnEnable() {
        this.UpdateGraphic();
    }

    private void OnDestroy() {
        this.button.onClick.RemoveAllListeners();

        BuildManager.OnGridModeStateChange -= UpdateGraphic;
    }

    private void UpdateGraphic() {
        if (!BuildManager.Instance) return;

        this.image.sprite = BuildManager.Instance.GridModeActivated ? this.gridActivatedSprite : this.gridDeactivatedSprite;
    }
}
