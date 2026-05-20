using DG.Tweening;
using Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating "À vendre" label shown above a prop listed for sale.
///
/// Two modes:
///   - Prefab  : assign a styled prefab on SaleActionsConfig.billboardPrefab with this
///               component on the root and its visualRoot/canvasGroup/label wired.
///   - Fallback: if no prefab is set, the visuals are built procedurally at runtime.
///
/// Positioning (robust to arbitrary pivots / heights / rotation / scale):
///   - if a child Transform named "BillboardAnchor" exists on the prop, it is used;
///   - else the top is computed from the combined world bounds of ALL renderers
///     (recomputed each time the label appears, so the build-in scale animation is
///     handled correctly).
///
/// Visibility is gated on the local player being within the prop's interaction range.
/// Effects: fade + scale-in on show, a constant vertical bob (world-space, so prop
/// rotation doesn't tilt it), and constant camera-facing.
/// </summary>
public class PropSaleBillboard : MonoBehaviour {
    [Header("Refs (auto-built if left empty)")]
    [SerializeField] private RectTransform   visualRoot;   // scaled / billboarded / bobbed
    [SerializeField] private CanvasGroup      canvasGroup;
    [SerializeField] private TextMeshProUGUI  label;

    [Header("Tuning")]
    [SerializeField] private float heightMargin = 0.35f;   // gap above the prop top
    [SerializeField] private float bobAmplitude = 0.08f;
    [SerializeField] private float bobDuration  = 1.4f;
    [SerializeField] private float rangePadding = 0.5f;    // extra over interaction range
    [SerializeField] private float worldScale   = 0.0075f; // procedural canvas scale
    [SerializeField] private Color reservedColor = new Color(1f, 0.78f, 0.3f);

    private PropBehaviourBase _prop;
    private Transform _anchor;        // optional explicit anchor on the prop
    private Vector3   _baseScale;
    private float     _anchorWorldY;  // computed top (world) when no explicit anchor
    private bool      _forSale;
    private bool      _visible;
    private float     _nextRangeCheck;

    // ── Setup ──────────────────────────────────────────────────────────────────

    /// <summary>Binds the billboard to its prop and prepares visuals (builds them if missing).</summary>
    public void Init(PropBehaviourBase prop) {
        _prop = prop;

        if (visualRoot == null || canvasGroup == null || label == null)
            BuildProceduralVisuals();

        _baseScale = visualRoot.localScale;
        _anchor    = FindAnchor(prop.transform);

        ApplyRenderOnTop();

        canvasGroup.alpha = 0f;
        _visible = false;
    }

    // Shared "always on top" material for the billboard backgrounds (ZTest Always).
    private static Material _onTopMaterial;

    /// <summary>
    /// Makes the billboard ignore depth so it isn't clipped by walls/geometry in
    /// front of it. Backgrounds use the UI/SaleBillboardOnTop shader; the TMP label
    /// gets its Z-test set to Always. Works for both prefab and procedural visuals.
    /// </summary>
    private void ApplyRenderOnTop() {
        if (_onTopMaterial == null) {
            Shader shader = Shader.Find("UI/SaleBillboardOnTop");
            if (shader != null) _onTopMaterial = new Material(shader) { name = "SaleBillboardOnTop (Runtime)" };
        }
        if (_onTopMaterial != null) {
            foreach (Image img in visualRoot.GetComponentsInChildren<Image>(true))
                img.material = _onTopMaterial;
        }

        if (label != null) {
            // TMP's UGUI shaders use ZTest [unity_GUIZTestMode] (canvas-controlled), so
            // setting _ZTestMode has no effect. Swapping to TMP's Overlay shader (ZTest
            // Always) is the reliable way to render the text on top. Keeps the same
            // distance-field properties/atlas, so the look is unchanged.
            Material fm = label.fontMaterial; // per-instance material
            if (fm != null) {
                Shader overlay = Shader.Find(fm.shader != null && fm.shader.name.Contains("Mobile")
                    ? "TextMeshPro/Mobile/Distance Field Overlay"
                    : "TextMeshPro/Distance Field Overlay");
                if (overlay != null) fm.shader = overlay;
            }
        }

        // Draw the canvas late so overlapping world geometry doesn't win the sort.
        Canvas c = visualRoot.GetComponent<Canvas>();
        if (c != null) { c.overrideSorting = true; c.sortingOrder = 1000; }
    }

    private static Transform FindAnchor(Transform propRoot) {
        foreach (Transform t in propRoot.GetComponentsInChildren<Transform>(true))
            if (t.name == "BillboardAnchor") return t;
        return null;
    }

    // ── Public state ─────────────────────────────────────────────────────────────

    /// <summary>Updates content + whether the prop is for sale. Visibility is then
    /// reconciled against the local player's range every frame.</summary>
    public void SetState(bool forSale, int price, string reservedByName) {
        _forSale = forSale;

        if (forSale && label != null) {
            if (!string.IsNullOrEmpty(reservedByName)) {
                label.text  = $"<size=20>RÉSERVÉ</size>\n<size=24>{reservedByName}</size>";
                label.color = reservedColor;
            } else if (price > 0) {
                label.text  = $"<size=20>À VENDRE</size>\n<size=30><b>{price}</b></size>";
                label.color = Color.white;
            } else {
                label.text  = "<size=20>À DONNER</size>\n<size=26><b>GRATUIT</b></size>";
                label.color = Color.white;
            }
        }

        ReconcileVisibility(true);
    }

    // ── Visibility / range ─────────────────────────────────────────────────────

    private void Update() {
        if (_prop == null) return;
        _nextRangeCheck -= Time.deltaTime;
        if (_nextRangeCheck > 0f) return;
        _nextRangeCheck = 0.2f;
        ReconcileVisibility(false);
    }

    private void ReconcileVisibility(bool immediate) {
        bool desired = _forSale && IsLocalPlayerInRange();
        if (desired && !_visible) Show();
        else if (!desired && _visible) Hide(immediate);
    }

    private bool IsLocalPlayerInRange() {
        PlayerController local = PlayerController.Local;
        if (local == null || _prop == null) return false;
        float range = _prop.GetRange() + rangePadding;
        return (local.transform.position - _prop.transform.position).sqrMagnitude <= range * range;
    }

    private void Show() {
        _visible = true;
        RecomputeAnchorHeight();

        DOTween.Kill(this);
        canvasGroup.alpha = 0f;
        visualRoot.localScale = _baseScale * 0.6f;
        canvasGroup.DOFade(1f, 0.25f).SetTarget(this);
        visualRoot.DOScale(_baseScale, 0.35f).SetEase(Ease.OutBack).SetTarget(this);
    }

    private void Hide(bool immediate) {
        _visible = false;
        DOTween.Kill(this);
        if (immediate || canvasGroup == null) {
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        } else {
            canvasGroup.DOFade(0f, 0.2f).SetTarget(this);
        }
    }

    // ── Positioning / billboarding ───────────────────────────────────────────────

    private void RecomputeAnchorHeight() {
        if (_anchor != null) return; // explicit anchor wins
        Renderer[] renderers = _prop.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) {
            _anchorWorldY = _prop.transform.position.y + heightMargin;
            return;
        }
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        _anchorWorldY = b.max.y + heightMargin;
    }

    private void LateUpdate() {
        if (!_visible || visualRoot == null || _prop == null) return;

        Vector3 basePos = _anchor != null
            ? _anchor.position
            : new Vector3(_prop.transform.position.x, _anchorWorldY, _prop.transform.position.z);

        float bob = Mathf.Sin(Time.time * (Mathf.PI * 2f / bobDuration)) * bobAmplitude;
        visualRoot.position = basePos + Vector3.up * bob;

        Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
        if (cam != null)
            visualRoot.rotation = Quaternion.LookRotation(visualRoot.position - cam.transform.position, Vector3.up);
    }

    private void OnDestroy() => DOTween.Kill(this);

    // ── Procedural fallback visuals ──────────────────────────────────────────────

    private void BuildProceduralVisuals() {
        var canvasGO = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        canvasGO.transform.SetParent(transform, false);
        visualRoot  = (RectTransform)canvasGO.transform;
        canvasGroup = canvasGO.GetComponent<CanvasGroup>();

        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        visualRoot.sizeDelta   = new Vector2(240, 90);
        visualRoot.localScale  = Vector3.one * worldScale;

        var bgGO = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGO.transform.SetParent(visualRoot, false);
        var bgRt = (RectTransform)bgGO.transform;
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        bgGO.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.85f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(visualRoot, false);
        var lblRt = (RectTransform)labelGO.transform;
        lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = new Vector2(8, 6); lblRt.offsetMax = new Vector2(-8, -6);
        label = labelGO.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize  = 28;
        label.color     = Color.white;
    }
}
