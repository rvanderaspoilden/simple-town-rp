using Sim;
using Sim.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimEditor {
    /// <summary>
    /// One-click builder for the NPC dialogue modal (<see cref="DialogueUI"/>). Operates directly on
    /// the HUD Manager prefab asset (load contents → build → save) so the result persists without a
    /// manual prefab save. Idempotent: re-running replaces the previously built panel.
    ///
    /// Layout: title (NPC name) · NPC line (wrapping) · options container (VerticalLayoutGroup) with a
    /// disabled option-button template cloned per response at runtime · close button. Same convention
    /// as <see cref="SaleUIBuilder"/> (panel left ACTIVE; the controller hides itself in Awake).
    /// </summary>
    public static class DialogueUIBuilder {
        private const string PrefabPath = "Assets/Resources/Prefabs/Managers/HUD Manager.prefab";
        private const string PanelName  = "DialogueUI (Built)";

        [MenuItem("Tools/The BROZ/Build Dialogue UI")]
        public static void Build() {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) {
                EditorUtility.DisplayDialog("Build Dialogue UI", $"Could not load prefab at {PrefabPath}.", "OK");
                return;
            }

            try {
                Transform parent = ResolveParent(root);
                if (parent == null) {
                    EditorUtility.DisplayDialog("Build Dialogue UI",
                        "Could not resolve a UI parent (DefaultViewUI / Canvas) in the HUD prefab.", "OK");
                    return;
                }

                DestroyExisting(parent, PanelName);
                BuildPanel(parent);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[DialogueUIBuilder] Built '{PanelName}' under '{parent.name}' and saved the HUD prefab.");
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Parents next to the existing merchant modal (same canvas layer), else DefaultViewUI/Canvas.</summary>
        private static Transform ResolveParent(GameObject root) {
            MerchantShopUI shop = root.GetComponentInChildren<MerchantShopUI>(true);
            if (shop != null) return shop.transform.parent;

            DefaultViewUI defaultView = root.GetComponentInChildren<DefaultViewUI>(true);
            if (defaultView != null) return defaultView.transform;

            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            return canvas != null ? canvas.transform : null;
        }

        private static void DestroyExisting(Transform parent, string name) {
            Transform existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        // ── Panel ────────────────────────────────────────────────────────────────

        private static void BuildPanel(Transform parent) {
            GameObject panel = CreatePanel(parent, PanelName, new Vector2(560, 460));
            var ui = panel.AddComponent<DialogueUI>();

            TMP_Text title = CreateLabel(panel.transform, "Title", new Vector2(0, 196), new Vector2(520, 44),
                "PNJ", 26, FontStyles.Bold, TextAlignmentOptions.Center);

            TMP_Text npcText = CreateLabel(panel.transform, "NpcText", new Vector2(0, 70), new Vector2(520, 200),
                "…", 20, FontStyles.Normal, TextAlignmentOptions.TopLeft);

            Button close = CreateButton(panel.transform, "CloseButton", new Vector2(244, 210), new Vector2(40, 40),
                "✕", new Color(0.45f, 0.20f, 0.20f), 22);

            RectTransform options = CreateOptionsContainer(panel.transform, "OptionsContainer",
                new Vector2(0, -110), new Vector2(520, 200));

            DialogueOptionButton template = CreateOptionTemplate(options, "OptionTemplate");

            SerializedObject so = new SerializedObject(ui);
            so.FindProperty("titleLabel").objectReferenceValue       = title;
            so.FindProperty("npcText").objectReferenceValue          = npcText;
            so.FindProperty("closeButton").objectReferenceValue      = close;
            so.FindProperty("optionsContainer").objectReferenceValue = options;
            so.FindProperty("optionTemplate").objectReferenceValue   = template;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── uGUI helpers (mirror SaleUIBuilder) ───────────────────────────────────

        private static GameObject CreatePanel(Transform parent, string name, Vector2 size) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            go.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.94f);
            return go;
        }

        private static TMP_Text CreateLabel(Transform parent, string name, Vector2 pos, Vector2 size,
                                            string text, float fontSize, FontStyles style, TextAlignmentOptions align) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = align;
            label.color = Color.white;
            return label;
        }

        private static RectTransform CreateOptionsContainer(Transform parent, string name, Vector2 pos, Vector2 size) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return rt;
        }

        private static DialogueOptionButton CreateOptionTemplate(Transform parent, string name) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(520, 48);

            go.GetComponent<Image>().color = new Color(0.20f, 0.22f, 0.28f, 1f);
            Button button = go.AddComponent<Button>();

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = 44;
            le.preferredHeight = 48;

            TMP_Text label = CreateLabel(go.transform, "Label", Vector2.zero, new Vector2(500, 44),
                "…", 20, FontStyles.Normal, TextAlignmentOptions.Center);
            RectTransform labelRt = (RectTransform)label.transform;
            labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(12, 2); labelRt.offsetMax = new Vector2(-12, -2);

            var optBtn = go.AddComponent<DialogueOptionButton>();
            SerializedObject so = new SerializedObject(optBtn);
            so.FindProperty("label").objectReferenceValue  = label;
            so.FindProperty("button").objectReferenceValue = button;
            so.ApplyModifiedPropertiesWithoutUndo();
            return optBtn;
        }

        private static Button CreateButton(Transform parent, string name, Vector2 pos, Vector2 size,
                                           string label, Color color, float fontSize) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            go.GetComponent<Image>().color = color;
            Button button = go.AddComponent<Button>();

            CreateLabel(go.transform, "Label", Vector2.zero, size, label, fontSize, FontStyles.Bold,
                TextAlignmentOptions.Center);
            return button;
        }
    }
}
