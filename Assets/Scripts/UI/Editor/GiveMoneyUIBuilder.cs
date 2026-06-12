using Sim;
using Sim.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimEditor {
    /// <summary>
    /// One-click builder for the player-to-player "give money" modal. Run
    /// "Tools/The BROZ/Build Give Money UI" with the scene (or prefab) containing
    /// the HUDManager open. It constructs the panel under the HUD canvas (or
    /// DefaultViewUI) and wires every serialized reference on the controller.
    /// Idempotent — re-running replaces the previously built panel. Mirrors
    /// SaleUIBuilder.cs to stay aligned with the existing visual style.
    /// </summary>
    public static class GiveMoneyUIBuilder {
        private const string PanelName = "GiveMoneyInputUI (Built)";

        [MenuItem("Tools/The BROZ/Build Give Money UI")]
        public static void Build() {
            HUDManager hud = ResolveHUDManager();
            if (hud == null) {
                EditorUtility.DisplayDialog("Build Give Money UI",
                    "No HUDManager found. Open the scene (or the HUD prefab) that contains the HUDManager, then run again.",
                    "OK");
                return;
            }

            DefaultViewUI defaultView = ResolveDefaultViewUI(hud);
            Transform parent = defaultView != null ? defaultView.transform : null;
            if (parent == null) {
                Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
                if (canvas == null) canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
                parent = canvas != null ? canvas.transform : null;
            }
            if (parent == null) {
                EditorUtility.DisplayDialog("Build Give Money UI",
                    "Could not resolve DefaultViewUI or a Canvas in the HUD. Open the HUD scene/prefab and run again.",
                    "OK");
                return;
            }

            DestroyExisting(parent, PanelName);
            GiveMoneyInputUI ui = BuildPanel(parent);

            // Leave the panel ACTIVE in the saved scene/prefab: the controller's
            // Awake hides itself at runtime, but Awake won't fire if we save it
            // inactive (same caveat as SaleUIBuilder).
            ui.gameObject.SetActive(true);

            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);

            // When invoked while a prefab is open in the prefab stage, mark
            // the stage's scene dirty too so the change can be saved via
            // assets-prefab-save / Ctrl+S.
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && !Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage.scene);

            Selection.activeObject = ui.gameObject;
            Debug.Log("[GiveMoneyUIBuilder] Built GiveMoneyInputUI under " +
                      $"'{parent.name}'. Save the scene/prefab to persist.");
        }

        // FindAnyObjectByType does NOT see GameObjects living inside the
        // currently-opened prefab stage. Check the stage first so this builder
        // is usable both when the HUD prefab is open and when an actual scene
        // containing the HUDManager is loaded.
        private static HUDManager ResolveHUDManager() {
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null) {
                HUDManager inStage = stage.prefabContentsRoot.GetComponentInChildren<HUDManager>(true);
                if (inStage != null) return inStage;
            }
            return Object.FindAnyObjectByType<HUDManager>(FindObjectsInactive.Include);
        }

        private static DefaultViewUI ResolveDefaultViewUI(HUDManager hud) {
            SerializedObject so = new SerializedObject(hud);
            SerializedProperty prop = so.FindProperty("defaultViewUI");
            if (prop != null && prop.objectReferenceValue is DefaultViewUI dv && dv != null) return dv;
            return Object.FindAnyObjectByType<DefaultViewUI>(FindObjectsInactive.Include);
        }

        private static void DestroyExisting(Transform parent, string name) {
            Transform existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        private static GiveMoneyInputUI BuildPanel(Transform parent) {
            GameObject panel = CreatePanel(parent, PanelName, new Vector2(420, 220));
            var ui = panel.AddComponent<GiveMoneyInputUI>();

            TMP_Text title = CreateLabel(panel.transform, "Title", new Vector2(0, 70), new Vector2(380, 50),
                "Donner de l'argent", 26, FontStyles.Bold);

            TMP_InputField input = CreateInputField(panel.transform, "AmountInput", new Vector2(0, 5), new Vector2(260, 56), "Montant…");

            Button confirm = CreateButton(panel.transform, "ConfirmButton", new Vector2(-95, -70), new Vector2(170, 56),
                "Donner", new Color(0.20f, 0.55f, 0.30f));
            Button cancel  = CreateButton(panel.transform, "CancelButton", new Vector2(95, -70), new Vector2(170, 56),
                "Annuler", new Color(0.45f, 0.20f, 0.20f));

            SerializedObject so = new SerializedObject(ui);
            so.FindProperty("titleLabel").objectReferenceValue    = title;
            so.FindProperty("amountInput").objectReferenceValue   = input;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("cancelButton").objectReferenceValue  = cancel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ui;
        }

        // ── uGUI helpers (identical to SaleUIBuilder for visual consistency) ─

        private static GameObject CreatePanel(Transform parent, string name, Vector2 size) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            Image bg = go.GetComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.10f, 0.94f);
            return go;
        }

        private static TMP_Text CreateLabel(Transform parent, string name, Vector2 pos, Vector2 size,
                                            string text, float fontSize, FontStyles style) {
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
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            return label;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, Vector2 pos, Vector2 size, string placeholder) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.10f);

            TMP_InputField input = go.AddComponent<TMP_InputField>();

            TMP_Text textArea = CreateChildText(go.transform, "Text", placeholder, 22, false);
            TMP_Text place    = CreateChildText(go.transform, "Placeholder", placeholder, 22, true);

            input.textViewport = (RectTransform)textArea.transform.parent;
            input.textComponent = textArea;
            input.placeholder = place;
            input.text = string.Empty;
            return input;
        }

        private static TMP_Text CreateChildText(Transform parent, string name, string text, float size, bool dim) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12, 4); rt.offsetMax = new Vector2(-12, -4);

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = dim ? text : string.Empty;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Left;
            label.color = dim ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, Vector2 pos, Vector2 size, string label, Color color) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            go.GetComponent<Image>().color = color;
            Button button = go.AddComponent<Button>();

            CreateLabel(go.transform, "Label", Vector2.zero, size, label, 20, FontStyles.Bold);
            return button;
        }
    }
}
