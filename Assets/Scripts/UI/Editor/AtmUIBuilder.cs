using Sim.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimEditor {
    /// <summary>
    /// One-click builder of the DIEGETIC ATM screen : run "Tools/The BROZ/Build ATM UI". It
    /// (re)builds the WORLD-SPACE canvas prefab <c>Resources/Prefabs/UI/AtmScreenUI.prefab</c>
    /// (Canvas WorldSpace + GraphicRaycaster + <see cref="AtmUI"/>), instantiated at runtime by
    /// AtmUI.GetOrCreate() and snapped onto the used ATM's Screen Anchor — the panel floats on
    /// the machine's screen instead of living in the 2D HUD. Idempotent — re-running overwrites
    /// the prefab. Also removes the legacy 2D HUD panel ("AtmUI (Built)") from the HUD prefab
    /// if a previous iteration left one there. Visual style mirrors GiveMoneyUIBuilder.
    /// </summary>
    public static class AtmUIBuilder {
        private const string PrefabPath     = "Assets/Resources/Prefabs/UI/AtmScreenUI.prefab";
        private const string RootName       = "AtmScreenUI";
        private const string LegacyHudPanel = "AtmUI (Built)";
        private const string HudPrefabPath  = "Assets/Resources/Prefabs/Managers/HUD Manager.prefab";

        // ~450×406 px à l'échelle 0.001 → 0.45 × 0.41 m : taille calée À LA MAIN dans la
        // scène (l'écran du mesh est en fait légèrement incliné — voir l'ancre inclinée de
        // 17° dans AtmSetup) ; remplace la mesure « dalle plate » initiale de la dalle
        // écran du SM_Bld_Wall_ATM_01 (mesurées par calibration) — le canvas la recouvre pile.
        private static readonly Vector2 CanvasSize = new Vector2(449.7357f, 405.5782f);
        private const float CanvasScale = 0.001f;

        // Palette « écran bancaire » : fond navy opaque, bandeau bleu, accents verts.
        private static readonly Color ScreenBg   = new Color(0.025f, 0.075f, 0.16f, 1f);
        private static readonly Color HeaderBg   = new Color(0.07f, 0.22f, 0.48f, 1f);
        private static readonly Color FieldBg    = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color FieldText  = new Color(0.49f, 0.99f, 0.60f, 1f);
        private static readonly Color BtnGreen   = new Color(0.12f, 0.45f, 0.24f, 1f);
        private static readonly Color BtnBlue    = new Color(0.12f, 0.30f, 0.48f, 1f);
        private static readonly Color BtnRed     = new Color(0.45f, 0.17f, 0.17f, 1f);

        [MenuItem("Tools/The BROZ/Build ATM UI")]
        public static void Build() {
            RemoveLegacyHudPanel();

            GameObject root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            try {
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;

                RectTransform rt = (RectTransform)root.transform;
                rt.sizeDelta = CanvasSize;
                rt.localScale = Vector3.one * CanvasScale;

                AtmUI ui = root.AddComponent<AtmUI>();

                // Fond opaque « écran allumé » + fin liseré clair (cadre de la dalle).
                Image frame = NewImage("Frame", root.transform, new Color(0.35f, 0.48f, 0.62f, 1f));
                frame.rectTransform.anchorMin = Vector2.zero; frame.rectTransform.anchorMax = Vector2.one;
                frame.rectTransform.offsetMin = Vector2.zero; frame.rectTransform.offsetMax = Vector2.zero;
                Image bg = NewImage("BG", root.transform, ScreenBg);
                bg.rectTransform.anchorMin = Vector2.zero; bg.rectTransform.anchorMax = Vector2.one;
                bg.rectTransform.offsetMin = new Vector2(4, 4); bg.rectTransform.offsetMax = new Vector2(-4, -4);

                // Bandeau banque en haut de l'écran.
                Image header = NewImage("Header", root.transform, HeaderBg);
                header.rectTransform.anchorMin = new Vector2(0f, 1f); header.rectTransform.anchorMax = Vector2.one;
                header.rectTransform.pivot = new Vector2(0.5f, 1f);
                header.rectTransform.offsetMin = new Vector2(4, -52); header.rectTransform.offsetMax = new Vector2(-4, -4);
                CreateLabel(header.transform, "BankName", Vector2.zero, new Vector2(400, 44),
                    "BROZ BANK", 24, FontStyles.Bold);

                // ── État « Insérez votre carte » ────────────────────────────────
                GameObject insertPanel = NewPanel("InsertPanel", root.transform);
                TMP_Text insertLabel = CreateLabel(insertPanel.transform, "InsertLabel", new Vector2(0, -4),
                    new Vector2(380, 180), "Insérez votre carte", 24, FontStyles.Bold);
                insertLabel.color = new Color(0.85f, 0.92f, 1f, 1f);

                // ── État CHOIX (Retirer / Déposer) ──────────────────────────────
                GameObject choicePanel = NewPanel("ChoicePanel", root.transform);
                choicePanel.SetActive(false);

                CreateLabel(choicePanel.transform, "ChoiceTitle", new Vector2(0, 100),
                    new Vector2(400, 30), "Que souhaitez-vous faire ?", 20, FontStyles.Bold);
                TMP_Text choiceBalances = CreateLabel(choicePanel.transform, "ChoiceBalances", new Vector2(0, 64),
                    new Vector2(430, 26), "En banque : —   ·   Sur vous : —", 15, FontStyles.Normal);
                choiceBalances.color = new Color(0.75f, 0.85f, 0.95f, 1f);

                Button chooseWithdraw = CreateButton(choicePanel.transform, "ChooseWithdraw", new Vector2(0, 8),
                    new Vector2(380, 52), "Retirer des espèces", BtnGreen, out _);
                Button chooseDeposit = CreateButton(choicePanel.transform, "ChooseDeposit", new Vector2(0, -52),
                    new Vector2(380, 52), "Déposer des espèces", BtnBlue, out _);

                // ── État OPÉRATION (partagé retrait / dépôt — titres et couleurs
                // dynamiques, AtmUI.ApplyStep) ──────────────────────────────────
                GameObject operationPanel = NewPanel("OperationPanel", root.transform);
                operationPanel.SetActive(false);

                TMP_Text opTitle = CreateLabel(operationPanel.transform, "OperationTitle", new Vector2(0, 122),
                    new Vector2(400, 28), "RETRAIT", 20, FontStyles.Bold);
                TMP_Text opBalances = CreateLabel(operationPanel.transform, "OperationBalances", new Vector2(0, 94),
                    new Vector2(430, 24), "En banque : —   ·   Sur vous : —", 15, FontStyles.Normal);
                opBalances.color = new Color(0.75f, 0.85f, 0.95f, 1f);

                TMP_InputField input = CreateInputField(operationPanel.transform, "AmountInput", new Vector2(0, 58),
                    new Vector2(310, 40), "Montant…", FieldBg, FieldText);

                // Pavé de montants prédéfinis (2 rangées × 3), dernier = MAX.
                int[] presetValues = { 20, 50, 100, 200, 500, 0 };
                var presetButtons = new Button[presetValues.Length];
                Color presetColor = new Color(0.16f, 0.22f, 0.34f, 1f);
                for (int i = 0; i < presetValues.Length; i++) {
                    float x = -130f + 130f * (i % 3);
                    float y = i < 3 ? 10f : -32f;
                    string label = presetValues[i] > 0 ? presetValues[i] + " €" : "MAX";
                    presetButtons[i] = CreateButton(operationPanel.transform, "Preset" + (presetValues[i] > 0 ? presetValues[i].ToString() : "Max"),
                        new Vector2(x, y), new Vector2(120, 38), label, presetColor, out _);
                }

                Button confirm = CreateButton(operationPanel.transform, "ConfirmButton", new Vector2(0, -90),
                    new Vector2(380, 44), "Retirer", BtnGreen, out TMP_Text confirmLabel);

                // ── Bouton commun bas d'écran (Annuler / Rendre la carte / Retour) ──
                Button close = CreateButton(root.transform, "CloseButton", new Vector2(0, -150),
                    new Vector2(380, 40), "Annuler", BtnRed, out TMP_Text closeLabel);

                // Ligne de feedback tout en bas : les toasts locaux (erreurs de pré-check et
                // refus serveur) y sont détournés pendant la session — voir AtmUI.OnLocalToast.
                // Vide au repos ; AtmUI la colore (rouge/vert) et l'efface après ~2,6 s.
                TMP_Text feedback = CreateLabel(root.transform, "FeedbackLabel", new Vector2(0, -186),
                    new Vector2(430, 26), string.Empty, 15, FontStyles.Bold);

                SerializedObject so = new SerializedObject(ui);
                so.FindProperty("insertPanel").objectReferenceValue            = insertPanel;
                so.FindProperty("insertLabel").objectReferenceValue            = insertLabel;
                so.FindProperty("choicePanel").objectReferenceValue            = choicePanel;
                so.FindProperty("choiceBalancesLabel").objectReferenceValue    = choiceBalances;
                so.FindProperty("chooseWithdrawButton").objectReferenceValue   = chooseWithdraw;
                so.FindProperty("chooseDepositButton").objectReferenceValue    = chooseDeposit;
                so.FindProperty("operationPanel").objectReferenceValue         = operationPanel;
                so.FindProperty("operationTitle").objectReferenceValue         = opTitle;
                so.FindProperty("operationBalancesLabel").objectReferenceValue = opBalances;
                so.FindProperty("amountInput").objectReferenceValue            = input;
                so.FindProperty("confirmButton").objectReferenceValue          = confirm;
                so.FindProperty("confirmLabel").objectReferenceValue           = confirmLabel;
                so.FindProperty("closeButton").objectReferenceValue            = close;
                so.FindProperty("closeLabel").objectReferenceValue             = closeLabel;
                so.FindProperty("feedbackLabel").objectReferenceValue          = feedback;
                SerializedProperty btnArr = so.FindProperty("presetButtons");
                SerializedProperty valArr = so.FindProperty("presetValues");
                btnArr.arraySize = presetButtons.Length;
                valArr.arraySize = presetValues.Length;
                for (int i = 0; i < presetButtons.Length; i++) {
                    btnArr.GetArrayElementAtIndex(i).objectReferenceValue = presetButtons[i];
                    valArr.GetArrayElementAtIndex(i).intValue = presetValues[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                // Racine ACTIVE dans le prefab : Awake doit tourner à l'Instantiate
                // (il pose Instance et cache le panneau).
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[AtmUIBuilder] Prefab world-space écrit : {PrefabPath}");
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Retire l'ancien panneau HUD 2D (itération précédente) du prefab HUD.</summary>
        private static void RemoveLegacyHudPanel() {
            GameObject contents = PrefabUtility.LoadPrefabContents(HudPrefabPath);
            if (contents == null) return;
            try {
                Transform legacy = FindDeep(contents.transform, LegacyHudPanel);
                if (legacy == null) return;
                Object.DestroyImmediate(legacy.gameObject);
                PrefabUtility.SaveAsPrefabAsset(contents, HudPrefabPath);
                Debug.Log($"[AtmUIBuilder] Ancien panneau HUD '{LegacyHudPanel}' retiré de {HudPrefabPath}");
            } finally {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Transform FindDeep(Transform root, string name) {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // ── uGUI helpers (base GiveMoneyUIBuilder, adaptés au style écran bancaire) ─

        /// <summary>Conteneur plein-écran sans visuel (regroupe un état de l'AtmUI).</summary>
        private static GameObject NewPanel(string n, Transform parent) {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private static Image NewImage(string n, Transform parent, Color color) {
            var go = new GameObject(n, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
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

        private static TMP_InputField CreateInputField(Transform parent, string name, Vector2 pos, Vector2 size,
                                                       string placeholder, Color bgColor, Color textColor) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = bgColor;

            TMP_InputField input = go.AddComponent<TMP_InputField>();

            TMP_Text textArea = CreateChildText(go.transform, "Text", placeholder, 20, false);
            textArea.color = textColor;
            TMP_Text place = CreateChildText(go.transform, "Placeholder", placeholder, 20, true);

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

        private static Button CreateButton(Transform parent, string name, Vector2 pos, Vector2 size,
                                           string label, Color color, out TMP_Text labelText) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            go.GetComponent<Image>().color = color;
            Button button = go.AddComponent<Button>();

            labelText = CreateLabel(go.transform, "Label", Vector2.zero, size, label, 19, FontStyles.Bold);
            return button;
        }
    }
}
