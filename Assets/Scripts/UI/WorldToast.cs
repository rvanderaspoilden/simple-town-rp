using System;
using DG.Tweening;
using Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single cozy world-space toast that floats above a world anchor (usually the local
/// player) and plays a soft scale-in → float-up → fade-out sequence (~1.2s), then
/// destroys itself. Visuals are built procedurally (frosted rounded panel + drop shadow +
/// TMP text) so there is no prefab dependency. Spawned and managed by WorldToastManager.
///
/// Generic: it only knows about a title line, an accented subtitle line and an accent
/// color — reuse it for "+1 Crédit Social", "+10 XP", etc.
/// </summary>
/// <summary>Visual/audio template applied to a toast. Neutral = the cozy default ;
/// Error/Success share a common style (color + icon + sound + animation).</summary>
public enum ToastKind { Neutral = 0, Error = 1, Success = 2 }

public class WorldToast : MonoBehaviour
{
    // Palette (cozy, warm, eco-friendly)
    private static readonly Color OffWhite  = new Color(0.96f, 0.94f, 0.88f, 1f);
    private static readonly Color PanelTint = new Color(0.12f, 0.14f, 0.18f, 0.82f); // dark gray-blue, translucent
    private static readonly Color ShadowCol = new Color(0f, 0f, 0f, 0.35f);

    // Shared error/success templates (color + icon).
    private static readonly Color ErrorColor   = new Color(0.95f, 0.38f, 0.34f, 1f);
    private static readonly Color SuccessColor = new Color(0.46f, 0.86f, 0.52f, 1f);
    private const string ErrorIcon   = "⚠ ";
    private const string SuccessIcon = "✓ ";

    private const float WorldScale = 0.0045f;

    private RectTransform _root;
    private RectTransform _content;       // holds the visuals; animated for kind FX (not touched by LateUpdate)
    private CanvasGroup    _group;
    private Transform      _anchor;
    private float          _heightOffset;
    private float          _rise;          // animated vertical float (world meters)
    private ToastKind      _kind;
    private Action         _onComplete;

    // ── Factory ──────────────────────────────────────────────────────────────────

    /// <summary>Builds a toast GameObject with its procedural visuals (not yet animated).</summary>
    public static WorldToast Create(string title, string subtitle, Color accent, ToastKind kind = ToastKind.Neutral)
    {
        var go = new GameObject("WorldToast");
        var toast = go.AddComponent<WorldToast>();
        toast.Build(title, subtitle, accent, kind);
        return toast;
    }

    private void Build(string title, string subtitle, Color accent, ToastKind kind)
    {
        _kind = kind;
        // Error/Success override the accent with their shared template color + add an icon.
        string icon = string.Empty;
        if (kind == ToastKind.Error)   { accent = ErrorColor;   icon = ErrorIcon; }
        else if (kind == ToastKind.Success) { accent = SuccessColor; icon = SuccessIcon; }
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1100;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;          // never blocks gameplay
        _group.interactable = false;

        _root = (RectTransform)transform;
        _root.localScale = Vector3.one * WorldScale;

        // Content wrapper: kind FX (shake/punch) animate THIS in local space, so they don't
        // fight the world-space follow + billboard applied to _root in LateUpdate.
        var contentGO = new GameObject("Content", typeof(RectTransform));
        contentGO.transform.SetParent(_root, false);
        _content = (RectTransform)contentGO.transform;
        _content.anchorMin = Vector2.zero; _content.anchorMax = Vector2.one;
        _content.offsetMin = Vector2.zero; _content.offsetMax = Vector2.zero;

        Sprite rounded = GetRoundedSprite();

        // Soft drop shadow (offset, slightly larger).
        var shadow = NewImage("Shadow", _content, rounded, ShadowCol);
        shadow.rectTransform.anchorMin = Vector2.zero; shadow.rectTransform.anchorMax = Vector2.one;
        shadow.rectTransform.offsetMin = new Vector2(-6f, -10f);
        shadow.rectTransform.offsetMax = new Vector2(6f, 2f);

        // Frosted translucent panel.
        var bg = NewImage("BG", _content, rounded, PanelTint);
        bg.rectTransform.anchorMin = Vector2.zero; bg.rectTransform.anchorMax = Vector2.one;
        bg.rectTransform.offsetMin = Vector2.zero; bg.rectTransform.offsetMax = Vector2.zero;

        // Text (title off-white, subtitle accented + bold + slightly oversized).
        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(_content, false);
        var lblRt = (RectTransform)labelGO.transform;
        lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = new Vector2(18f, 14f); lblRt.offsetMax = new Vector2(-18f, -14f);

        var label = labelGO.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.richText = true;
        label.fontSize = 26f;
        label.color = OffWhite;
        string accentHex = ColorUtility.ToHtmlStringRGB(accent);
        if (string.IsNullOrEmpty(subtitle))
        {
            // Simple ligne. Neutral = off-white ; Error/Success = couleur + icône du template.
            label.text = _kind == ToastKind.Neutral
                ? $"<size=26>{title}</size>"
                : $"<size=26><b><color=#{accentHex}>{icon}{title}</color></b></size>";
        }
        else
        {
            label.text = $"<size=22>{icon}{title}</size>\n<size=30><b><color=#{accentHex}>{subtitle}</color></b></size>";
        }

        // Auto-size the panel to hug the text (+ padding) so the rounded background always
        // wraps the content cleanly, whatever the message length.
        label.ForceMeshUpdate();
        Vector2 pref = label.GetPreferredValues();
        const float padX = 44f, padY = 32f;
        _root.sizeDelta = new Vector2(Mathf.Ceil(pref.x) + padX, Mathf.Ceil(pref.y) + padY);

        _group.alpha = 0f;
    }

    // ── Playback ───────────────────────────────────────────────────────────────────

    /// <summary>Anchors above <paramref name="anchor"/> at the given world-height offset and runs the sequence.</summary>
    public void Play(Transform anchor, float heightOffset, Action onComplete)
    {
        _anchor = anchor;
        _heightOffset = heightOffset;
        _onComplete = onComplete;

        Vector3 baseScale = Vector3.one * WorldScale;
        _root.localScale = baseScale * 0.6f;

        // Smooth float-up over the full lifetime.
        DOTween.To(() => _rise, v => _rise = v, 0.6f, 2.6f).SetEase(Ease.OutCubic).SetTarget(this);

        // scale-in (0.15s, soft) → fade-in → hold → gentle fade-out. Total ~2.6s.
        var seq = DOTween.Sequence().SetTarget(this);
        seq.Append(_group.DOFade(1f, 0.12f));
        seq.Join(_root.DOScale(baseScale, 0.15f).SetEase(Ease.OutBack));
        seq.AppendInterval(2.0f);
        seq.Append(_group.DOFade(0f, 0.45f).SetEase(Ease.InCubic));
        seq.OnComplete(() =>
        {
            _onComplete?.Invoke();
            Destroy(gameObject);
        });

        // Kind FX on the content wrapper (local space — independent of the billboard).
        PlayKindAnimation();
    }

    /// <summary>Error = sharp horizontal shake ; Success = bouncy punch. Plays just after
    /// the toast appears. Animates _content (local) so it never fights the LateUpdate billboard.</summary>
    private void PlayKindAnimation()
    {
        if (_content == null) return;
        switch (_kind)
        {
            case ToastKind.Error:
                _content.DOShakeAnchorPos(0.45f, new Vector2(22f, 0f), 16, 90f, false, true)
                        .SetDelay(0.14f).SetTarget(this);
                break;
            case ToastKind.Success:
                _content.DOPunchScale(Vector3.one * 0.18f, 0.42f, 9, 0.8f)
                        .SetDelay(0.14f).SetTarget(this);
                break;
        }
    }

    private void LateUpdate()
    {
        if (_anchor == null) return;

        _root.position = _anchor.position + Vector3.up * (_heightOffset + _rise);

        Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
        if (cam != null)
            _root.rotation = Quaternion.LookRotation(_root.position - cam.transform.position, Vector3.up);
    }

    private void OnDestroy() => DOTween.Kill(this);

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static Image NewImage(string imgName, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(imgName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // ── Procedural rounded 9-slice sprite ──────────────────────────────────────────

    private static Sprite _roundedSprite;

    private static Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null) return _roundedSprite;

        const int size = 64;
        const int radius = 22;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
            float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(radius - dist + 0.5f); // 1px anti-aliased edge
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        _roundedSprite = Sprite.Create(
            tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return _roundedSprite;
    }
}
