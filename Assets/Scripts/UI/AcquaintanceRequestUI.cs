using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// Popup shown to player B when someone wants to make acquaintance. Self-built
    /// Screen Space Overlay (no scene wiring, like HoverNameTooltip). Accept/Refuse
    /// send a C2S_AcquaintanceResponse back to the server. Identity is never shown
    /// here — only that "un Broz" wants to connect.
    /// </summary>
    public class AcquaintanceRequestUI : MonoBehaviour {
        private static AcquaintanceRequestUI _instance;
        public static AcquaintanceRequestUI Instance => _instance != null ? _instance : (_instance = Build());

        private GameObject _panel;
        private uint _fromNetId;

        public void ShowRequest(uint fromNetId) {
            _fromNetId = fromNetId;
            _panel.SetActive(true);
        }

        public void Hide() {
            if (_panel != null) _panel.SetActive(false);
        }

        private void Respond(bool accepted) {
            if (NetworkClient.active) {
                NetworkClient.Send(new C2S_AcquaintanceResponse { fromNetId = _fromNetId, accepted = accepted });
            }
            Hide();
        }

        private static AcquaintanceRequestUI Build() {
            GameObject root = new GameObject("AcquaintanceRequestUI");
            DontDestroyOnLoad(root);

            AcquaintanceRequestUI ui = root.AddComponent<AcquaintanceRequestUI>();

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1090;
            root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            root.AddComponent<GraphicRaycaster>();

            // Panel anchored bottom-center.
            ui._panel = new GameObject("Panel");
            ui._panel.transform.SetParent(root.transform, false);
            RectTransform panelRect = ui._panel.AddComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 180f);
            panelRect.sizeDelta = new Vector2(420f, 130f);
            Image bg = ui._panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.82f);

            CreateLabel(ui._panel.transform, "Un Broz souhaite faire connaissance.",
                new Vector2(0f, -16f), new Vector2(400f, 40f), new Vector2(0.5f, 1f));

            CreateButton(ui._panel.transform, "Accepter", new Vector2(-100f, 20f),
                new Color(0.20f, 0.55f, 0.25f, 1f), () => ui.Respond(true));
            CreateButton(ui._panel.transform, "Refuser", new Vector2(100f, 20f),
                new Color(0.55f, 0.22f, 0.22f, 1f), () => ui.Respond(false));

            ui._panel.SetActive(false);
            return ui;
        }

        private static void CreateLabel(Transform parent, string text, Vector2 anchoredPos, Vector2 size, Vector2 anchor) {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 18f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        private static void CreateButton(Transform parent, string text, Vector2 anchoredPos, Color color, UnityEngine.Events.UnityAction onClick) {
            GameObject go = new GameObject($"Button_{text}");
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(160f, 44f);
            Image img = go.AddComponent<Image>();
            img.color = color;
            Button button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            CreateLabel(go.transform, text, Vector2.zero, new Vector2(160f, 44f), new Vector2(0.5f, 0.5f));
        }
    }
}
