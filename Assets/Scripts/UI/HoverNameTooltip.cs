using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// Lightweight cursor-following tooltip showing a hovered character's name.
    /// Self-contained: lazily builds its own Screen Space Overlay canvas (no scene
    /// wiring needed, same procedural approach as WorldToast). Driven each frame by
    /// CameraManager.ManageHover via the static Show/Hide API.
    /// </summary>
    public class HoverNameTooltip : MonoBehaviour {
        private static HoverNameTooltip _instance;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _panel;
        private CanvasGroup _group;
        private TextMeshProUGUI _label;

        private static readonly Vector2 CursorOffset = new Vector2(22f, -22f);
        private static readonly Vector2 Padding = new Vector2(18f, 12f);
        private const float LabelFontSize = 20f;

        public static void Show(string fullName) {
            if (string.IsNullOrEmpty(fullName)) { Hide(); return; }
            Instance._label.text = fullName;
            Instance.Resize();
            Instance._group.alpha = 1f;
            Instance.Reposition();
        }

        public static void Hide() {
            if (_instance != null) _instance._group.alpha = 0f;
        }

        private static HoverNameTooltip Instance {
            get {
                if (_instance == null) _instance = Build();
                return _instance;
            }
        }

        private static HoverNameTooltip Build() {
            GameObject root = new GameObject("HoverNameTooltip");
            DontDestroyOnLoad(root);

            HoverNameTooltip t = root.AddComponent<HoverNameTooltip>();

            t._canvas = root.AddComponent<Canvas>();
            t._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            t._canvas.sortingOrder = 1100;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            t._canvasRect = root.GetComponent<RectTransform>();

            GameObject panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(root.transform, false);
            t._panel = panelGo.AddComponent<RectTransform>();
            t._panel.anchorMin = t._panel.anchorMax = new Vector2(0.5f, 0.5f);
            t._panel.pivot = new Vector2(0f, 1f); // top-left → box sits lower-right of cursor

            Image bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.72f);
            bg.raycastTarget = false;

            t._group = panelGo.AddComponent<CanvasGroup>();
            t._group.alpha = 0f;
            t._group.interactable = false;
            t._group.blocksRaycasts = false;

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(panelGo.transform, false);
            t._label = textGo.AddComponent<TextMeshProUGUI>();
            t._label.fontSize = LabelFontSize;
            t._label.color = Color.white;
            t._label.alignment = TextAlignmentOptions.Center;
            t._label.raycastTarget = false;
            t._label.richText = true;
            RectTransform labelRect = t._label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(Padding.x * 0.5f, Padding.y * 0.5f);
            labelRect.offsetMax = new Vector2(-Padding.x * 0.5f, -Padding.y * 0.5f);

            return t;
        }

        private void Resize() {
            Vector2 preferred = _label.GetPreferredValues(_label.text);
            _panel.sizeDelta = preferred + Padding;
        }

        private void LateUpdate() {
            if (_group != null && _group.alpha > 0f) Reposition();
        }

        private void Reposition() {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, Input.mousePosition, null, out Vector2 local);

            Vector2 pos = local + CursorOffset;

            // Keep the box inside the canvas bounds.
            Rect canvas = _canvasRect.rect;
            Vector2 size = _panel.sizeDelta;
            float halfW = canvas.width * 0.5f;
            float halfH = canvas.height * 0.5f;
            // pivot is top-left: x extends right, y extends down.
            if (pos.x + size.x > halfW) pos.x = local.x - CursorOffset.x - size.x;
            if (pos.y - size.y < -halfH) pos.y = local.y + size.y;

            _panel.anchoredPosition = pos;
        }
    }
}
