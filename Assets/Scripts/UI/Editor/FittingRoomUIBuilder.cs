using Sim;
using Sim.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimEditor {
    /// <summary>
    /// One-click builder for the clothing-shop fitting room overlay (<see cref="FittingRoomUI"/>) —
    /// HUD DÉDIÉ plein écran (maquette boutique). Operates on the HUD Manager prefab (load
    /// contents → build → save). Idempotent: re-running replaces the previously built panel.
    ///
    /// IMPORTANT : le panneau est construit en FRÈRE de « Default View » (pas enfant) — la vue
    /// par défaut fait partie des gameHudElements masqués par HidesGameHud ; un enfant se
    /// masquerait lui-même.
    ///
    /// Layout : header (titre+sous-titre) + argent en haut à droite · colonne gauche = onglets
    /// de catégorie + GRILLE de cartes scrollable (template désactivé) · panneau de DÉTAIL à
    /// droite (icône, nom, prix, description, bouton Essayer) · footer droite = Total +
    /// Acheter/Annuler · hint bas-gauche (Échap / rotation au drag).
    /// </summary>
    public static class FittingRoomUIBuilder {
        private const string PrefabPath = "Assets/Resources/Prefabs/Managers/HUD Manager.prefab";
        private const string PanelName  = "FittingRoomUI (Built)";

        private static readonly Color PanelColor  = new Color(0.07f, 0.07f, 0.09f, 0.94f);
        private static readonly Color CardColor   = new Color(0.14f, 0.15f, 0.18f, 1f);
        private static readonly Color AccentColor = new Color(0.22f, 0.42f, 0.65f, 1f);

        [MenuItem("Tools/The BROZ/Build Fitting Room UI")]
        public static void Build() {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) {
                EditorUtility.DisplayDialog("Build Fitting Room UI", $"Could not load prefab at {PrefabPath}.", "OK");
                return;
            }

            try {
                // Frère de « Default View » : son parent direct.
                DefaultViewUI defaultView = root.GetComponentInChildren<DefaultViewUI>(true);
                Transform parent = defaultView != null ? defaultView.transform.parent
                    : root.GetComponentInChildren<Canvas>(true)?.transform;
                if (parent == null) {
                    EditorUtility.DisplayDialog("Build Fitting Room UI", "No UI parent found in HUD prefab.", "OK");
                    return;
                }

                // Supprime TOUTE version précédente, où qu'elle soit (ancien emplacement = enfant
                // de Default View).
                foreach (FittingRoomUI old in root.GetComponentsInChildren<FittingRoomUI>(true)) {
                    Object.DestroyImmediate(old.gameObject);
                }

                BuildPanel(parent);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[FittingRoomUIBuilder] Built '{PanelName}' under '{parent.name}' and saved the HUD prefab.");
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── Panel ────────────────────────────────────────────────────────────────

        private static void BuildPanel(Transform parent) {
            // Racine plein écran SANS image (le personnage 3D reste visible au centre, et un
            // raycast plein écran bloquerait la rotation au drag).
            GameObject panel = new GameObject(PanelName, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            Stretch((RectTransform)panel.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ui = panel.AddComponent<FittingRoomUI>();

            // Header (haut-gauche) : titre + sous-titre.
            GameObject header = CreateCard(panel.transform, "Header", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -96f), new Vector2(420f, -20f));
            TMP_Text title = CreateLabel(header.transform, "Title", "Boutique de vêtements", 22, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft);
            Stretch(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 34f), new Vector2(-16f, -8f));
            TMP_Text subtitle = CreateLabel(header.transform, "Subtitle", "Essayez, achetez, équipez !", 14,
                FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            subtitle.color = new Color(0.75f, 0.75f, 0.78f, 1f);
            Stretch(subtitle.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 8f), new Vector2(-16f, -40f));

            // Argent (haut-droite).
            GameObject moneyCard = CreateCard(panel.transform, "Money", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-220f, -68f), new Vector2(-20f, -20f));
            TMP_Text money = CreateLabel(moneyCard.transform, "Amount", "0 $", 20, FontStyles.Bold,
                TextAlignmentOptions.Center);
            money.color = new Color(1f, 0.85f, 0.35f, 1f);
            Stretch(money.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Colonne gauche : onglets + grille.
            GameObject left = CreateCard(panel.transform, "LeftPanel", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(20f, 20f), new Vector2(510f, -110f));

            RectTransform tabs = CreateRect(left.transform, "Tabs");
            Stretch(tabs, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, -88f), new Vector2(-8f, -8f));
            HorizontalLayoutGroup hlg = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            Button tabTemplate = CreateTabTemplate(tabs);

            (RectTransform grid, FittingRoomCard cardTemplate) = CreateScrollGrid(left.transform);

            // Panneau de détail (droite).
            GameObject detail = CreateCard(panel.transform, "DetailPanel", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-380f, 220f), new Vector2(-20f, -110f));
            Image detailIcon = CreateImage(detail.transform, "Icon");
            RectTransform diRt = (RectTransform)detailIcon.transform;
            diRt.anchorMin = diRt.anchorMax = new Vector2(0.5f, 1f);
            diRt.pivot = new Vector2(0.5f, 1f);
            diRt.anchoredPosition = new Vector2(0f, -16f);
            diRt.sizeDelta = new Vector2(160f, 160f);
            detailIcon.preserveAspect = true;

            TMP_Text detailName = CreateLabel(detail.transform, "Name", "Article", 20, FontStyles.Bold,
                TextAlignmentOptions.Center);
            Stretch(detailName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -220f), new Vector2(-12f, -186f));

            TMP_Text detailPrice = CreateLabel(detail.transform, "Price", "0 $", 18, FontStyles.Bold,
                TextAlignmentOptions.Center);
            detailPrice.color = new Color(1f, 0.85f, 0.35f, 1f);
            Stretch(detailPrice.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -252f), new Vector2(-12f, -222f));

            // Caractéristiques de l'article (catégorie, genre…) — texte multi-lignes rempli
            // par FittingRoomUI.ShowDetail.
            GameObject statsCard = CreateCard(detail.transform, "Stats", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(12f, -336f), new Vector2(-12f, -260f));
            statsCard.GetComponent<Image>().color = new Color(0.11f, 0.11f, 0.14f, 1f);
            TMP_Text detailStats = CreateLabel(statsCard.transform, "Lines", "Catégorie : …\nGenre : …", 14,
                FontStyles.Normal, TextAlignmentOptions.TopLeft);
            detailStats.color = new Color(0.85f, 0.85f, 0.88f, 1f);
            Stretch(detailStats.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -8f));

            TMP_Text detailDesc = CreateLabel(detail.transform, "Description", "…", 14, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            detailDesc.color = new Color(0.8f, 0.8f, 0.83f, 1f);
            Stretch(detailDesc.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 64f), new Vector2(-12f, -344f));

            (Button tryButton, TMP_Text tryLabel) = CreateButton(detail.transform, "TryButton", "Essayer", AccentColor);
            Stretch((RectTransform)tryButton.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 12f), new Vector2(-12f, 56f));

            // Footer (bas-droite) : total + Acheter / Annuler.
            TMP_Text total = CreateLabel(panel.transform, "Total", "Total : 0 $", 20, FontStyles.Bold,
                TextAlignmentOptions.MidlineRight);
            Stretch(total.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-380f, 166f), new Vector2(-24f, 196f));

            (Button buy, TMP_Text buyLabel) = CreateButton(panel.transform, "BuyButton", "Acheter",
                new Color(0.18f, 0.45f, 0.24f, 1f));
            Stretch((RectTransform)buy.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-380f, 112f), new Vector2(-20f, 158f));

            (Button cancel, TMP_Text _) = CreateButton(panel.transform, "CancelButton", "Annuler",
                new Color(0.45f, 0.20f, 0.20f, 1f));
            Stretch((RectTransform)cancel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-380f, 62f), new Vector2(-20f, 104f));

            // Hint (bas-gauche, hors panneau gauche).
            TMP_Text hint = CreateLabel(panel.transform, "Hint", "Échap : annuler    ·    Clic gauche maintenu : faire pivoter", 14,
                FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            hint.color = new Color(0.8f, 0.8f, 0.83f, 0.9f);
            Stretch(hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(530f, 20f), new Vector2(1100f, 48f));

            SerializedObject so = new SerializedObject(ui);
            so.FindProperty("titleLabel").objectReferenceValue        = title;
            so.FindProperty("moneyLabel").objectReferenceValue        = money;
            so.FindProperty("tabsContainer").objectReferenceValue     = tabs;
            so.FindProperty("tabTemplate").objectReferenceValue       = tabTemplate;
            so.FindProperty("gridContainer").objectReferenceValue     = grid;
            so.FindProperty("cardTemplate").objectReferenceValue      = cardTemplate;
            so.FindProperty("detailRoot").objectReferenceValue        = detail;
            so.FindProperty("detailIcon").objectReferenceValue        = detailIcon;
            so.FindProperty("detailName").objectReferenceValue        = detailName;
            so.FindProperty("detailPrice").objectReferenceValue       = detailPrice;
            so.FindProperty("detailDescription").objectReferenceValue = detailDesc;
            so.FindProperty("detailStats").objectReferenceValue      = detailStats;
            so.FindProperty("tryButton").objectReferenceValue         = tryButton;
            so.FindProperty("tryButtonLabel").objectReferenceValue    = tryLabel;
            so.FindProperty("totalLabel").objectReferenceValue        = total;
            so.FindProperty("buyButton").objectReferenceValue         = buy;
            so.FindProperty("buyButtonLabel").objectReferenceValue    = buyLabel;
            so.FindProperty("cancelButton").objectReferenceValue      = cancel;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Grille scrollable + templates ─────────────────────────────────────────

        private static (RectTransform, FittingRoomCard) CreateScrollGrid(Transform parent) {
            GameObject scrollGo = new GameObject("ArticleGrid", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            Stretch((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -94f));
            scrollGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.6f);

            GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            Stretch((RectTransform)viewportGo.transform, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            viewportGo.GetComponent<Image>().color = Color.white;
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            GameObject contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            RectTransform content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            GridLayoutGroup glg = contentGo.AddComponent<GridLayoutGroup>();
            // 3 colonnes de largeur ÉGALE remplissant toute la grille. Largeurs fixes par
            // construction : LeftPanel 490 → ArticleGrid 474 → Viewport 466 ; moins le padding
            // (2×8) et 2 espacements (2×8) = 434 répartis sur 3 cellules.
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 3;
            glg.cellSize = new Vector2(434f / 3f, 150f);
            glg.spacing = new Vector2(8f, 8f);
            glg.padding = new RectOffset(8, 8, 8, 8);
            glg.childAlignment = TextAnchor.UpperLeft;
            ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewportGo.transform;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            FittingRoomCard template = CreateCardTemplate(content);
            return (content, template);
        }

        private static FittingRoomCard CreateCardTemplate(Transform parent) {
            GameObject go = new GameObject("CardTemplate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image background = go.GetComponent<Image>();
            background.color = CardColor;
            Button button = go.AddComponent<Button>();

            Image icon = CreateImage(go.transform, "Icon");
            RectTransform iconRt = (RectTransform)icon.transform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -8f);
            iconRt.sizeDelta = new Vector2(108f, 108f);
            icon.preserveAspect = true;

            TMP_Text price = CreateLabel(go.transform, "Price", "0 $", 14, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(price.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(4f, 4f), new Vector2(-4f, 28f));

            // Coche « au panier » (coin haut-droit).
            GameObject check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            check.transform.SetParent(go.transform, false);
            RectTransform checkRt = (RectTransform)check.transform;
            checkRt.anchorMin = checkRt.anchorMax = new Vector2(1f, 1f);
            checkRt.pivot = new Vector2(1f, 1f);
            checkRt.anchoredPosition = new Vector2(-4f, -4f);
            checkRt.sizeDelta = new Vector2(24f, 24f);
            check.GetComponent<Image>().color = new Color(0.25f, 0.75f, 0.35f, 1f);
            TMP_Text checkLabel = CreateLabel(check.transform, "Label", "✓", 16, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(checkLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            check.SetActive(false);

            var card = go.AddComponent<FittingRoomCard>();
            SerializedObject so = new SerializedObject(card);
            so.FindProperty("background").objectReferenceValue = background;
            so.FindProperty("icon").objectReferenceValue       = icon;
            so.FindProperty("checkmark").objectReferenceValue  = check;
            so.FindProperty("priceLabel").objectReferenceValue = price;
            so.FindProperty("button").objectReferenceValue     = button;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            return card;
        }

        private static Button CreateTabTemplate(Transform parent) {
            GameObject go = new GameObject("TabTemplate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = CardColor;
            Button button = go.AddComponent<Button>();

            // Icône de catégorie AU-DESSUS du label (maquette) — sprite posé au runtime
            // (FittingRoomUI.BuildTabs : premier article de la catégorie).
            Image icon = CreateImage(go.transform, "Icon");
            RectTransform iconRt = (RectTransform)icon.transform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -8f);
            iconRt.sizeDelta = new Vector2(38f, 38f);
            icon.preserveAspect = true;

            TMP_Text label = CreateLabel(go.transform, "Label", "Onglet", 13, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 6f), new Vector2(-2f, 28f));
            go.SetActive(false);
            return button;
        }

        // ── uGUI helpers ─────────────────────────────────────────────────────────

        private static GameObject CreateCard(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform, anchorMin, anchorMax, offsetMin, offsetMax);
            go.GetComponent<Image>().color = PanelColor;
            return go;
        }

        private static RectTransform CreateRect(Transform parent, string name) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image CreateImage(Transform parent, string name) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Image>();
        }

        private static (Button, TMP_Text) CreateButton(Transform parent, string name, string text, Color color) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            Button button = go.AddComponent<Button>();
            TMP_Text label = CreateLabel(go.transform, "Label", text, 18, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return (button, label);
        }

        private static TMP_Text CreateLabel(Transform parent, string name, string text, float fontSize,
            FontStyles style, TextAlignmentOptions align) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = align;
            label.color = Color.white;
            return label;
        }

        private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax) {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
