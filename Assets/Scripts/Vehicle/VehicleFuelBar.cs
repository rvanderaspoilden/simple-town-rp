using Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barre de progression monde affichée au-dessus d'un véhicule pendant le ravitaillement :
/// un petit label « Réservoir » et, dessous, une jauge qui se remplit avec le carburant.
/// Construite par programme (canvas world-space + billboard), comme <see cref="WorldToast"/>.
///
/// Réutilisable : <see cref="Create"/> l'instancie, <see cref="SetProgress"/> met à jour le
/// remplissage et la réaffiche (fondu) ; elle se masque seule après un court délai d'inactivité.
/// </summary>
public class VehicleFuelBar : MonoBehaviour {
    private const float WorldScale  = 0.0045f;
    private const float HoldTime    = 2.5f;   // secondes d'affichage après le dernier remplissage
    private const float FadeSpeed   = 6f;
    private const float BarWidth    = 260f;   // largeur du fond de jauge (px UI)
    private const float BarInset    = 4f;     // marge intérieure du fill

    private static readonly Color OffWhite  = new Color(0.96f, 0.94f, 0.88f, 1f);
    private static readonly Color PanelTint = new Color(0.12f, 0.14f, 0.18f, 0.82f);
    private static readonly Color TrackTint = new Color(0f, 0f, 0f, 0.55f);

    private Transform   _anchor;
    private float       _heightOffset;
    private CanvasGroup _group;
    private RectTransform _fill;
    private float       _visibleUntil;

    public static VehicleFuelBar Create(Transform anchor, float heightOffset) {
        var go = new GameObject("VehicleFuelBar");
        var bar = go.AddComponent<VehicleFuelBar>();
        bar.Build(anchor, heightOffset);
        return bar;
    }

    private void Build(Transform anchor, float heightOffset) {
        _anchor = anchor;
        _heightOffset = heightOffset;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1100;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
        _group.alpha = 0f;

        var root = (RectTransform)transform;
        root.localScale = Vector3.one * WorldScale;
        root.sizeDelta = new Vector2(300f, 86f);

        // Panneau de fond (lisibilité).
        var panel = NewImage("Panel", root, PanelTint);
        panel.rectTransform.anchorMin = Vector2.zero; panel.rectTransform.anchorMax = Vector2.one;
        panel.rectTransform.offsetMin = new Vector2(-8f, -8f); panel.rectTransform.offsetMax = new Vector2(8f, 8f);

        // Label « Réservoir » en haut.
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(root, false);
        var lblRt = (RectTransform)labelGo.transform;
        lblRt.anchorMin = new Vector2(0.5f, 0.5f); lblRt.anchorMax = new Vector2(0.5f, 0.5f);
        lblRt.pivot = new Vector2(0.5f, 0.5f);
        lblRt.sizeDelta = new Vector2(300f, 36f);
        lblRt.anchoredPosition = new Vector2(0f, 22f);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.fontSize = 26f;
        label.color = OffWhite;
        label.text = "Réservoir";

        // Fond de jauge.
        var track = NewImage("Track", root, TrackTint);
        track.rectTransform.anchorMin = new Vector2(0.5f, 0.5f); track.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        track.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        track.rectTransform.sizeDelta = new Vector2(BarWidth, 24f);
        track.rectTransform.anchoredPosition = new Vector2(0f, -16f);

        // Remplissage (ancré à gauche du fond, largeur pilotée par SetProgress).
        var fill = NewImage("Fill", track.rectTransform, Color.green);
        _fill = fill.rectTransform;
        _fill.anchorMin = new Vector2(0f, 0f); _fill.anchorMax = new Vector2(0f, 1f);
        _fill.pivot = new Vector2(0f, 0.5f);
        _fill.anchoredPosition = new Vector2(BarInset, 0f);
        _fill.sizeDelta = new Vector2(0f, -BarInset * 2f);

        SetProgress(0f);
        _group.alpha = 0f; // démarre masquée jusqu'au premier remplissage
    }

    /// <summary>Met à jour le remplissage [0..1], recolore (rouge→vert) et réaffiche la barre.</summary>
    public void SetProgress(float t) {
        t = Mathf.Clamp01(t);
        if (_fill != null) {
            _fill.sizeDelta = new Vector2((BarWidth - BarInset * 2f) * t, -BarInset * 2f);
            var img = _fill.GetComponent<Image>();
            if (img != null) img.color = Color.Lerp(new Color(0.9f, 0.3f, 0.2f), new Color(0.4f, 0.85f, 0.45f), t);
        }
        _visibleUntil = Time.time + HoldTime;
    }

    private void LateUpdate() {
        if (_anchor == null) { Destroy(gameObject); return; }

        var root = (RectTransform)transform;
        root.position = _anchor.position + Vector3.up * _heightOffset;
        Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
        if (cam != null) root.rotation = Quaternion.LookRotation(root.position - cam.transform.position, Vector3.up);

        float target = Time.time < _visibleUntil ? 1f : 0f;
        if (_group != null) _group.alpha = Mathf.MoveTowards(_group.alpha, target, FadeSpeed * Time.deltaTime);
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
