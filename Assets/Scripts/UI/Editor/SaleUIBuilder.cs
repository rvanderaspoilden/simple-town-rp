using Sim;
using Sim.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimEditor {
    /// <summary>
    /// One-click builder for the two player-to-player sale UIs (price input + buy
    /// confirm). Run "Tools/The BROZ/Build Sale UIs" with the scene (or prefab)
    /// containing the HUDManager open. It constructs both panels under the HUD
    /// canvas and wires every serialized reference (on the controllers and on
    /// HUDManager). Idempotent — re-running replaces the previously built panels.
    /// </summary>
    public static class SaleUIBuilder {
        private const string PricePanelName = "SalePriceInputUI (Built)";
        private const string BuyPanelName   = "BuyConfirmUI (Built)";

        [MenuItem("Tools/The BROZ/Build Sale UIs")]
        public static void Build() {
            HUDManager hud = Object.FindAnyObjectByType<HUDManager>(FindObjectsInactive.Include);
            if (hud == null) {
                EditorUtility.DisplayDialog("Build Sale UIs",
                    "No HUDManager found. Open the scene (or the HUD prefab) that contains the HUDManager, then run again.",
                    "OK");
                return;
            }

            // Parent the modals under DefaultViewUI (the in-game HUD layer that is
            // active during normal gameplay). Falls back to the HUD canvas if not found.
            DefaultViewUI defaultView = ResolveDefaultViewUI(hud);
            Transform parent = defaultView != null ? defaultView.transform : null;
            if (parent == null) {
                Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
                if (canvas == null) canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
                parent = canvas != null ? canvas.transform : null;
            }
            if (parent == null) {
                EditorUtility.DisplayDialog("Build Sale UIs",
                    "Could not resolve DefaultViewUI or a Canvas in the HUD. Open the HUD scene/prefab and run again.",
                    "OK");
                return;
            }

            // Replace any previously built panels (idempotency).
            DestroyExisting(parent, PricePanelName);
            DestroyExisting(parent, BuyPanelName);

            SalePriceInputUI priceUI = BuildPricePanel(parent);
            BuyConfirmUI     buyUI   = BuildBuyPanel(parent);

            // Wire HUDManager references.
            SerializedObject so = new SerializedObject(hud);
            so.FindProperty("salePriceInputUI").objectReferenceValue = priceUI;
            so.FindProperty("buyConfirmUI").objectReferenceValue     = buyUI;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);

            // IMPORTANT: leave the panels ACTIVE in the saved scene. Each controller's
            // Awake subscribes to its trigger event and then hides itself at runtime —
            // if we saved them inactive, Awake would never run and the modals would
            // never appear. (They will look visible in edit mode; that's expected.)
            priceUI.gameObject.SetActive(true);
            buyUI.gameObject.SetActive(true);

            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);

            Selection.activeObject = priceUI.gameObject;
            Debug.Log("[SaleUIBuilder] Built SalePriceInputUI + BuyConfirmUI under " +
                      $"'{parent.name}' and wired HUDManager. Save the scene/prefab to persist.");
        }

        /// <summary>Resolves the DefaultViewUI HUDManager actually references, falling back to a scene search.</summary>
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

        // ── Panels ─────────────────────────────────────────────────────────────

        private static SalePriceInputUI BuildPricePanel(Transform parent) {
            GameObject panel = CreatePanel(parent, PricePanelName, new Vector2(420, 220));
            var ui = panel.AddComponent<SalePriceInputUI>();

            TMP_Text title = CreateLabel(panel.transform, "Title", new Vector2(0, 70), new Vector2(380, 50),
                "Mettre en vente", 26, FontStyles.Bold);

            TMP_InputField input = CreateInputField(panel.transform, "PriceInput", new Vector2(0, 5), new Vector2(260, 56), "Prix…");

            Button confirm = CreateButton(panel.transform, "ConfirmButton", new Vector2(-95, -70), new Vector2(170, 56),
                "Mettre en vente", new Color(0.20f, 0.55f, 0.30f));
            Button cancel  = CreateButton(panel.transform, "CancelButton", new Vector2(95, -70), new Vector2(170, 56),
                "Annuler", new Color(0.45f, 0.20f, 0.20f));

            SerializedObject so = new SerializedObject(ui);
            so.FindProperty("titleLabel").objectReferenceValue    = title;
            so.FindProperty("priceInput").objectReferenceValue    = input;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("cancelButton").objectReferenceValue  = cancel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ui;
        }

        private static BuyConfirmUI BuildBuyPanel(Transform parent) {
            GameObject panel = CreatePanel(parent, BuyPanelName, new Vector2(420, 260));
            var ui = panel.AddComponent<BuyConfirmUI>();

            TMP_Text title  = CreateLabel(panel.transform, "Title", new Vector2(0, 90), new Vector2(380, 44),
                "Article", 26, FontStyles.Bold);
            TMP_Text price  = CreateLabel(panel.transform, "Price", new Vector2(0, 45), new Vector2(380, 40),
                "0 💰", 24, FontStyles.Normal);
            TMP_Text status = CreateLabel(panel.transform, "Status", new Vector2(0, 5), new Vector2(380, 30),
                "", 18, FontStyles.Italic);

            Button confirm = CreateButton(panel.transform, "ConfirmButton", new Vector2(-95, -80), new Vector2(170, 56),
                "Acheter", new Color(0.20f, 0.55f, 0.30f));
            Button cancel  = CreateButton(panel.transform, "CancelButton", new Vector2(95, -80), new Vector2(170, 56),
                "Annuler", new Color(0.45f, 0.20f, 0.20f));

            SerializedObject so = new SerializedObject(ui);
            so.FindProperty("titleLabel").objectReferenceValue    = title;
            so.FindProperty("priceLabel").objectReferenceValue    = price;
            so.FindProperty("statusLabel").objectReferenceValue   = status;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("cancelButton").objectReferenceValue  = cancel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ui;
        }

        // ── uGUI helpers ─────────────────────────────────────────────────────────

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
