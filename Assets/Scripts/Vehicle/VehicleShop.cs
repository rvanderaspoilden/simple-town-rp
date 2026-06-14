using Interaction;
using Mirror;
using Sim;
using Sim.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Action = Sim.Interactables.Action;

/// <summary>
/// Véhicule d'EXPOSITION (concession) — OBJET DE SCÈNE NON-RÉSEAU (pas de NetworkIdentity ni de
/// VehicleController). Visuel + interaction « Acheter » + billboard de prix de proximité (façon
/// ShopDisplay). L'achat est validé/exécuté côté serveur par <see cref="VehicleSystemBootstrap"/>.
///
/// Le prix, le modèle et le prefab viennent du <see cref="VehicleConfig"/> assigné. Le serveur
/// re-résout la config par son id (via DatabaseManager) pour valider le prix de façon autoritaire.
/// </summary>
public class VehicleShop : MonoBehaviour, IInteractable {
    [Tooltip("Config du véhicule en vente (prix, modèle, prefab).")]
    [SerializeField] private VehicleConfig config;
    [SerializeField] private float interactionRange = 3.5f;
    [Tooltip("Hauteur du billboard de prix au-dessus du véhicule.")]
    [SerializeField] private float billboardHeight = 2.2f;

    private VehicleConfig _config;
    private Action _buyAction;

    // Billboard (construit par code, façon WorldToast / PropSaleBillboard).
    private Transform _billboard;
    private CanvasGroup _billboardGroup;
    private bool _billboardVisible;
    private float _nextRangeCheck;

    private int Price => _config != null ? _config.price : 0;

    private void Awake() {
        _config = config;
        _buyAction = Action.CreateRuntime(ActionTypeEnum.BUY, "Acheter",
            Resources.Load<Action>("Configurations/Actions/BUY")?.Icon);
        _buyAction.OnExecute += OnBuyExecuted;
        BuildBillboard();
    }

    private void OnDestroy() {
        if (_buyAction != null) _buyAction.OnExecute -= OnBuyExecuted;
    }

    // ── IInteractable ───────────────────────────────────────────────────────────────
    public float GetRange()         => interactionRange;
    public bool  IsInteractable()   => _buyAction != null;
    public bool  IsRightClickOnly() => false;
    public void  StopInteraction()  { }
    public Action[] GetActions(bool withPriority = false)
        => _buyAction != null ? new[] { _buyAction } : System.Array.Empty<Action>();

    private void OnBuyExecuted(Action _) {
        // Vérif de fonds CÔTÉ CLIENT pour un retour immédiat (toast). Le serveur revalide.
        PlayerController local = PlayerController.Local;
        if (local == null) return;
        PlayerBankAccount bank = local.GetComponent<PlayerBankAccount>();
        if (bank != null && bank.Money < Price) {
            WorldToastManager.ShowError("Fonds insuffisants");
            return;
        }
        if (_config == null) return;
        NetworkClient.Send(new C2S_BuyVehicle { configId = _config.id });
    }

    // ── Billboard de prix (proximité) ───────────────────────────────────────────────
    private void BuildBillboard() {
        var go = new GameObject("PriceBillboard", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        _billboard = go.transform;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1000;
        _billboardGroup = go.GetComponent<CanvasGroup>();
        _billboardGroup.blocksRaycasts = false;
        _billboardGroup.interactable = false;
        _billboardGroup.alpha = 0f;

        var rt = (RectTransform)_billboard;
        rt.sizeDelta = new Vector2(260f, 96f);
        rt.localScale = Vector3.one * 0.01f;

        var bg = NewImage("BG", _billboard, new Color(0.08f, 0.08f, 0.10f, 0.85f));
        bg.rectTransform.anchorMin = Vector2.zero; bg.rectTransform.anchorMax = Vector2.one;
        bg.rectTransform.offsetMin = Vector2.zero; bg.rectTransform.offsetMax = Vector2.zero;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(_billboard, false);
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8f, 6f); lrt.offsetMax = new Vector2(-8f, -6f);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.color = Color.white;
        string model = _config != null && !string.IsNullOrEmpty(_config.modelName) ? _config.modelName : "Véhicule";
        label.text = $"<size=22>{model}</size>\n<size=18>À VENDRE</size>\n<size=30><b>{Price}</b></size>";
    }

    private void Update() {
        _nextRangeCheck -= Time.deltaTime;
        if (_nextRangeCheck > 0f) return;
        _nextRangeCheck = 0.2f;
        bool desired = IsLocalPlayerInRange();
        if (desired != _billboardVisible) {
            _billboardVisible = desired;
            if (_billboardGroup != null) _billboardGroup.alpha = desired ? 1f : 0f;
        }
    }

    private bool IsLocalPlayerInRange() {
        PlayerController local = PlayerController.Local;
        if (local == null) return false;
        float r = interactionRange + 1.5f; // un peu plus large que l'interaction (« à l'approche »)
        return (local.transform.position - transform.position).sqrMagnitude <= r * r;
    }

    private void LateUpdate() {
        if (!_billboardVisible || _billboard == null) return;
        _billboard.position = transform.position + Vector3.up * billboardHeight;
        Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
        if (cam != null)
            _billboard.rotation = Quaternion.LookRotation(_billboard.position - cam.transform.position, Vector3.up);
    }

    private static Image NewImage(string n, Transform parent, Color color) {
        var go = new GameObject(n, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }
}
