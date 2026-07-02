using Interaction;
using Mirror;
using Sim;
using Sim.Building;
using Sim.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Action = Sim.Interactables.Action;

/// <summary>
/// "For Sale" sign at the entrance of a land parcel — a NON-NETWORK scene object (no
/// NetworkIdentity), same pattern as <see cref="VehicleShop"/>. Shows a proximity billboard and a
/// single "Consulter" action that opens <see cref="Sim.UI.LandParcelInfoUI"/> in every state:
/// available (Acheter / Louer), owned (Vendre, owner-only) or rented (Résilier le bail,
/// tenant-only). The buy/rent/sell/terminate decisions all flow from that modal and are
/// validated/executed server-side by <see cref="LandSystemBootstrap"/>, which broadcasts the
/// resulting occupancy.
///
/// Price, rent and label come from the parcel's <see cref="Sim.Scriptables.LandConfig"/>; the
/// server re-resolves the config by parcelId to validate amounts authoritatively.
/// </summary>
public class ForSaleSign : MonoBehaviour, IInteractable {
    [Tooltip("The land parcel this sign sells. Required.")]
    [SerializeField] private LandParcelController parcel;
    [SerializeField] private float interactionRange = 3.5f;
    [Tooltip("Height of the price billboard above the sign.")]
    [SerializeField] private float billboardHeight = 2.6f;

    private Action _consultAction;

    private Transform _billboard;
    private CanvasGroup _billboardGroup;
    private TextMeshProUGUI _billboardLabel;
    private bool _billboardVisible;
    private float _nextRangeCheck;

    private int Price => parcel != null && parcel.Config != null ? parcel.Config.price : 0;
    private int Rent  => parcel != null && parcel.Config != null ? parcel.Config.rentPrice : 0;

    private void Awake() {
        _consultAction = Action.CreateRuntime(ActionTypeEnum.LOOK, "Consulter",
            Resources.Load<Action>("Configurations/Actions/LOOK")?.Icon);
        _consultAction.OnExecute += OnConsultExecuted;

        BuildBillboard();
    }

    private void OnEnable() {
        if (parcel != null) parcel.OnClientStateChanged += OnStateChanged;
        OnStateChanged();
    }

    private void OnDisable() {
        if (parcel != null) parcel.OnClientStateChanged -= OnStateChanged;
    }

    private void OnDestroy() {
        if (_consultAction != null) _consultAction.OnExecute -= OnConsultExecuted;
    }

    private void OnStateChanged() {
        RefreshBillboardText();
        UpdateBillboardVisibility(); // hide immediately once bought/rented, even if player stands in range
    }

    // The billboard only advertises an AVAILABLE lot — hidden once owned or rented.
    private bool IsAvailable() => parcel != null && !parcel.IsOwned && !parcel.IsRented;

    // ── IInteractable ───────────────────────────────────────────────────────────────
    public float GetRange()         => interactionRange;
    public bool  IsRightClickOnly() => false;
    public void  StopInteraction()  { }

    // Always consultable — the modal adapts its actions to the current occupancy.
    public bool IsInteractable() => parcel != null && _consultAction != null;

    public Action[] GetActions(bool withPriority = false) {
        if (parcel == null || _consultAction == null) return System.Array.Empty<Action>();
        return new[] { _consultAction };
    }

    private void OnConsultExecuted(Action _) {
        if (parcel == null) return;
        Sim.UI.LandParcelInfoUI.Instance?.ShowFor(parcel);
    }

    // ── Price billboard (proximity) ───────────────────────────────────────────────
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
        rt.sizeDelta = new Vector2(280f, 110f);
        rt.localScale = Vector3.one * 0.01f;

        var bg = NewImage("BG", _billboard, new Color(0.08f, 0.08f, 0.10f, 0.85f));
        bg.rectTransform.anchorMin = Vector2.zero; bg.rectTransform.anchorMax = Vector2.one;
        bg.rectTransform.offsetMin = Vector2.zero; bg.rectTransform.offsetMax = Vector2.zero;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(_billboard, false);
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8f, 6f); lrt.offsetMax = new Vector2(-8f, -6f);
        _billboardLabel = labelGo.AddComponent<TextMeshProUGUI>();
        _billboardLabel.alignment = TextAlignmentOptions.Center;
        _billboardLabel.enableWordWrapping = false;
        _billboardLabel.color = Color.white;
        RefreshBillboardText();
    }

    private void RefreshBillboardText() {
        if (_billboardLabel == null) return;
        string title = parcel != null && parcel.Config != null && !string.IsNullOrEmpty(parcel.Config.displayName)
            ? parcel.Config.displayName : "Terrain";
        _billboardLabel.text = $"<size=22>{title}</size>\n<size=18>À VENDRE / À LOUER</size>\n<size=22><b>{Price}</b> · <size=16>loyer {Rent}</size></size>";
    }

    private void Update() {
        _nextRangeCheck -= Time.deltaTime;
        if (_nextRangeCheck > 0f) return;
        _nextRangeCheck = 0.2f;
        UpdateBillboardVisibility();
    }

    private void UpdateBillboardVisibility() {
        bool desired = IsAvailable() && IsLocalPlayerInRange();
        if (desired == _billboardVisible) return;
        _billboardVisible = desired;
        if (_billboardGroup != null) _billboardGroup.alpha = desired ? 1f : 0f;
    }

    private bool IsLocalPlayerInRange() {
        PlayerController local = PlayerController.Local;
        if (local == null) return false;
        float r = interactionRange + 1.5f;
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
