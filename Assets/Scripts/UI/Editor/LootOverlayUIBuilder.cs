using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UIEditor {
    /// <summary>
    /// One-clic : construit l'overlay « Récupération » (LootPanelUI) dans le prefab
    /// HUD Manager — conteneur/loot à GAUCHE, inventaire (poche) DUPLIQUÉ à DROITE —
    /// puis DÉSACTIVE l'ancien panneau flottant du canal Prop (Props Container Panel).
    /// Relançable : l'overlay existant est reconstruit de zéro à chaque exécution.
    /// Styles clonés de l'existant (Card.png, Container Frame.png, Montserrat,
    /// template de slot poche avec outlines + drop highlight).
    /// </summary>
    public static class LootOverlayUIBuilder {
        private const string PrefabPath = "Assets/Resources/Prefabs/Managers/HUD Manager.prefab";

        // Palette maquette : panneaux bleu nuit translucides, liseré clair fin, titres blancs.
        private static readonly Color DimColor        = new Color(0.015f, 0.022f, 0.038f, 0.78f);
        private static readonly Color PanelBgColor    = new Color(0.055f, 0.068f, 0.090f, 0.94f);
        private static readonly Color PanelEdgeColor  = new Color(0.80f, 0.84f, 0.90f, 0.30f);
        private static readonly Color PanelTitleColor = Color.white;
        private static readonly Color SoftTextColor   = new Color(0.78f, 0.80f, 0.84f, 1f);
        private static readonly Color SeparatorColor  = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color DetailBgColor   = new Color(0.100f, 0.123f, 0.160f, 1f);
        private static readonly Color AccentColor     = new Color(0.753f, 0.557f, 0.404f, 1f);

        /// <summary>Pose les icon-buttons tri/optimisation dans les grilles de l'HUD
        /// INVENTAIRE : rangée « POCHES » (InventoryUI) + barre de titre du panneau
        /// colis/sac (ContainerPanelUI canal Item). Relançable (boutons reconstruits).</summary>
        [MenuItem("Tools/The BROZ/Build Inventory Grid Buttons")]
        public static void BuildInventoryGridButtons() {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try {
                Sprite card = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/Frames/Card.png");
                Sprite sortIcon  = EnsureGeneratedIcon("icon_sort_az",      DrawSortIcon);
                Sprite stackIcon = EnsureGeneratedIcon("icon_stack_merge", DrawStackIcon);

                // ── Rangée POCHES (InventoryUI) ──────────────────────────────────
                Transform header = prefabRoot.transform.Find(
                    "Inventory Group/Player Inventory Panel/Inventory Content/Pockets Section/Poches Header");
                if (header == null) throw new System.Exception("Poches Header introuvable.");
                RemoveIfExists(header, "Sort Button");
                RemoveIfExists(header, "Optimize Button");
                Button pSort = IconButton(header, "Sort Button", card, sortIcon, new Vector2(-30, 0));
                Button pOpt  = IconButton(header, "Optimize Button", card, stackIcon, new Vector2(0, 0));
                ResizeIconButton(pSort, 24); ResizeIconButton(pOpt, 24);

                var inv = prefabRoot.GetComponentInChildren<InventoryUI>(true);
                var soInv = new SerializedObject(inv);
                soInv.FindProperty("sortPocketButton").objectReferenceValue     = pSort;
                soInv.FindProperty("optimizePocketButton").objectReferenceValue = pOpt;
                soInv.ApplyModifiedPropertiesWithoutUndo();

                // ── Barre de titre du panneau colis/sac (canal Item) ─────────────
                Transform panel = prefabRoot.transform.Find(
                    "Inventory Group/Player Inventory Panel/Inventory Content/Item Container Panel/Root/Panel");
                if (panel == null) throw new System.Exception("Item Container Panel/Root/Panel introuvable.");
                RemoveIfExists(panel, "Sort Button");
                RemoveIfExists(panel, "Optimize Button");
                Button cSort = IconButton(panel, "Sort Button", card, sortIcon, new Vector2(-34, 0));
                Button cOpt  = IconButton(panel, "Optimize Button", card, stackIcon, new Vector2(-4, 0));
                ResizeIconButton(cSort, 26); ResizeIconButton(cOpt, 26);

                var itemPanel = prefabRoot.transform
                    .Find("Inventory Group/Player Inventory Panel/Inventory Content/Item Container Panel")
                    .GetComponent<ContainerPanelUI>();
                var soPanel = new SerializedObject(itemPanel);
                soPanel.FindProperty("sortButton").objectReferenceValue     = cSort;
                soPanel.FindProperty("optimizeButton").objectReferenceValue = cOpt;
                soPanel.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[LootOverlayUIBuilder] Boutons tri/optimisation posés dans l'HUD inventaire → " + PrefabPath);
            }
            finally {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void RemoveIfExists(Transform parent, string childName) {
            Transform t = parent.Find(childName);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        private static void ResizeIconButton(Button b, float size) {
            var rt = (RectTransform)b.transform;
            rt.sizeDelta = new Vector2(size, size);
        }

        [MenuItem("Tools/The BROZ/Build Loot Overlay UI")]
        public static void Build() {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try {
                BuildInto(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[LootOverlayUIBuilder] Loot Overlay construit + Props Container Panel désactivé → " + PrefabPath);
            }
            finally {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void BuildInto(GameObject prefabRoot) {
            Transform group = prefabRoot.transform.Find("Inventory Group");
            if (group == null) throw new System.Exception("Inventory Group introuvable dans le prefab HUD.");

            // Références de style existantes.
            Sprite card  = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/Frames/Card.png");
            TMP_FontAsset bold    = FindFont("Montserrat-Bold SDF");
            TMP_FontAsset regular = FindFont("Montserrat-Regular 2 SDF") ?? bold;
            Transform pocketTemplate = group.Find(
                "Player Inventory Panel/Inventory Content/Pockets Section/Pocket Grid/Pocket Slot Template");
            if (card == null || bold == null || pocketTemplate == null)
                throw new System.Exception("Style source manquant (Card/Montserrat/Pocket Slot Template).");

            // Reconstruit de zéro.
            Transform existing = group.Find("Loot Overlay");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // ── Racine (GO ACTIF porteur du composant ; Root enfant toggle) ──────
            GameObject overlayGo = NewUi("Loot Overlay", group);
            Stretch(overlayGo);
            var panel = overlayGo.AddComponent<LootPanelUI>();

            GameObject rootGo = NewUi("Root", overlayGo.transform);
            Stretch(rootGo);
            // Canvas imbriqué : rendu AU-DESSUS de la minimap (canvas override order 10),
            // sous le menu contextuel (1000) et la tooltip (1200). LootPanelUI ré-asserte
            // l'overrideSorting à chaque ouverture (quirk Unity sur canvas désactivé).
            Canvas rootCanvas = rootGo.AddComponent<Canvas>();
            rootCanvas.overrideSorting = true;
            rootCanvas.sortingOrder = 20;
            rootGo.AddComponent<GraphicRaycaster>();

            // Voile léger : le monde reste visible (maquette), mais capte les clics.
            GameObject dim = NewUi("Dim", rootGo.transform);
            Stretch(dim);
            Image dimImg = dim.AddComponent<Image>();
            dimImg.color = DimColor;

            // ── « ÉCHAP Fermer » haut-droite (bouton) — pas de titre d'écran (feedback) ──
            GameObject closeHint = NewUi("Close Hint", rootGo.transform);
            SetRect(closeHint, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -40), new Vector2(230, 44));
            Image hintBg = closeHint.AddComponent<Image>();
            hintBg.color = new Color(0, 0, 0, 0.001f); // cible de clic invisible
            Button closeBtn = closeHint.AddComponent<Button>();
            closeBtn.transition = Selectable.Transition.None;

            GameObject chip = NewUi("Key Chip", closeHint.transform);
            SetRect(chip, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(96, 34));
            Image chipImg = chip.AddComponent<Image>();
            chipImg.sprite = card; chipImg.type = Image.Type.Sliced; chipImg.pixelsPerUnitMultiplier = 2.2f;
            chipImg.color = new Color(0.85f, 0.87f, 0.90f, 1f);
            TextStretch(chip.transform, "Label", "ÉCHAP", bold, 16, new Color(0.12f, 0.14f, 0.18f, 1f),
                TextAlignmentOptions.Center, FontStyles.Bold);
            Text(closeHint.transform, "Fermer", "Fermer", regular, 19, Color.white,
                TextAlignmentOptions.MidlineLeft, new Vector2(108, 0), new Vector2(120, 34), FontStyles.Normal);

            // Icônes générées (blanc sur transparent, teintables) : tri + optimisation.
            Sprite sortIcon  = EnsureGeneratedIcon("icon_sort_az",      DrawSortIcon);
            Sprite stackIcon = EnsureGeneratedIcon("icon_stack_merge", DrawStackIcon);

            // ── Panneau CONTENEUR (gauche) ────────────────────────────────────────
            GameObject left = BuildFramedPanel("Container Panel", rootGo.transform, card,
                new Vector2(0, 0.5f), new Vector2(120, 300), 500);
            GameObject leftRow = BuildTitleRow(left.transform, bold, "CONTENEUR", out TMP_Text leftTitle, out TMP_Text leftCount);
            (Button leftSort, Button leftOpt) = AddTitleRowButtons(leftRow.transform, card, sortIcon, stackIcon, leftCount);
            BuildSeparator(left.transform);

            (GameObject slotsScroll, GameObject slots, LayoutElement slotsScrollLe) =
                BuildScrollableGrid(left.transform, "Slots Scroll", "Slots",
                    cellSize: 100, spacing: 8, pad: 8, columns: 4, card);

            ItemSlot slotTemplate = CloneSlotTemplate(pocketTemplate, slots.transform, "Slot Template", rejectsNonPocketable: false);

            // Légende des interactions (remplace l'encart de détail — la tooltip suffit).
            TMP_Text hints = TextLayout(left.transform, "Hints",
                "Double-clic : prendre   ·   Glisser-déposer : déplacer", regular, 13,
                new Color(0.60f, 0.63f, 0.68f, 1f), TextAlignmentOptions.Midline, FontStyles.Italic);

            // Bouton « Tout prendre (F) ».
            GameObject takeAll = NewUi("Take All Button", left.transform);
            Image taImg = takeAll.AddComponent<Image>();
            taImg.sprite = card; taImg.type = Image.Type.Sliced; taImg.pixelsPerUnitMultiplier = 2.24f;
            taImg.color = AccentColor;
            Button taBtn = takeAll.AddComponent<Button>();
            var taLe = takeAll.AddComponent<LayoutElement>();
            taLe.minHeight = 46; taLe.preferredHeight = 46;
            TextStretch(takeAll.transform, "Label", "TOUT PRENDRE (F)", bold, 17, Color.white,
                TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);

            // ── Panneau MON INVENTAIRE (droite, DUPLIQUÉ) ─────────────────────────
            GameObject right = BuildFramedPanel("Inventory Panel", rootGo.transform, card,
                new Vector2(1, 0.5f), new Vector2(-120, 300), 500);
            GameObject rightRow = BuildTitleRow(right.transform, bold, "MON INVENTAIRE", out TMP_Text rightTitle, out TMP_Text rightCount);
            (Button rightSort, Button rightOpt) = AddTitleRowButtons(rightRow.transform, card, sortIcon, stackIcon, rightCount);
            BuildSeparator(right.transform);

            (GameObject pocketScroll, GameObject pocketGrid, LayoutElement pocketScrollLe) =
                BuildScrollableGrid(right.transform, "Pocket Scroll", "Pocket Grid",
                    cellSize: 100, spacing: 12, pad: 8, columns: 4, card);

            ItemSlot pocketSlotTemplate = CloneSlotTemplate(pocketTemplate, pocketGrid.transform,
                "Pocket Slot Template", rejectsNonPocketable: true);

            // ── Câblage du composant ─────────────────────────────────────────────
            var so = new SerializedObject(panel);
            so.FindProperty("root").objectReferenceValue               = rootGo;
            so.FindProperty("rootCanvas").objectReferenceValue         = rootCanvas;
            so.FindProperty("containerTitle").objectReferenceValue     = leftTitle;
            so.FindProperty("containerCount").objectReferenceValue     = leftCount;
            so.FindProperty("slotsContainer").objectReferenceValue     = slots.transform;
            so.FindProperty("slotTemplate").objectReferenceValue       = slotTemplate;
            so.FindProperty("slotsScrollElement").objectReferenceValue = slotsScrollLe;
            so.FindProperty("sortContainerButton").objectReferenceValue     = leftSort;
            so.FindProperty("optimizeContainerButton").objectReferenceValue = leftOpt;
            so.FindProperty("sortPocketButton").objectReferenceValue        = rightSort;
            so.FindProperty("optimizePocketButton").objectReferenceValue    = rightOpt;
            so.FindProperty("takeAllButton").objectReferenceValue      = taBtn;
            so.FindProperty("inventoryCount").objectReferenceValue     = rightCount;
            so.FindProperty("pocketGrid").objectReferenceValue         = pocketGrid.transform;
            so.FindProperty("pocketSlotTemplate").objectReferenceValue = pocketSlotTemplate;
            so.FindProperty("pocketScrollElement").objectReferenceValue = pocketScrollLe;
            so.FindProperty("closeButton").objectReferenceValue        = closeBtn;
            Transform minimap = prefabRoot.transform.Find("Default View/Minimap");
            so.FindProperty("minimapRoot").objectReferenceValue        = minimap != null ? minimap.gameObject : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            rootGo.SetActive(false);

            // ── L'ancien panneau flottant du canal Prop est REMPLACÉ par l'overlay ──
            Transform oldProp = group.Find("Props Container Panel");
            if (oldProp != null) oldProp.gameObject.SetActive(false);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static GameObject NewUi(string name, Transform parent) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            return go;
        }

        private static void Stretch(GameObject go) {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void SetRect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 size) {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        /// <summary>Panneau latéral style maquette : fond bleu nuit arrondi + liseré clair fin
        /// (deux images « ignoreLayout » empilées sous le contenu), VLG auto-hauteur (fitter).
        /// Pivot Y = 1 → les DEUX panneaux partagent la même ligne de HAUT (maquette),
        /// quelle que soit leur hauteur de contenu.</summary>
        private static GameObject BuildFramedPanel(string name, Transform parent, Sprite card,
            Vector2 sideAnchor, Vector2 pos, float width) {
            GameObject go = NewUi(name, parent);
            SetRect(go, sideAnchor, sideAnchor, new Vector2(sideAnchor.x, 1f), pos, new Vector2(width, 600));
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(22, 22, 18, 24);
            vl.spacing = 10;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Liseré (couche 1) puis fond (couche 2), hors layout — rendus sous le contenu.
            GameObject border = NewUi("Border", go.transform);
            Stretch(border);
            border.AddComponent<LayoutElement>().ignoreLayout = true;
            Image bImg = border.AddComponent<Image>();
            bImg.sprite = card; bImg.type = Image.Type.Sliced; bImg.pixelsPerUnitMultiplier = 1.1f;
            bImg.color = PanelEdgeColor;
            bImg.raycastTarget = false;

            GameObject bg = NewUi("Bg", go.transform);
            Stretch(bg);
            var bgRt = (RectTransform)bg.transform;
            bgRt.offsetMin = new Vector2(2, 2); bgRt.offsetMax = new Vector2(-2, -2);
            bg.AddComponent<LayoutElement>().ignoreLayout = true;
            Image bgImg = bg.AddComponent<Image>();
            bgImg.sprite = card; bgImg.type = Image.Type.Sliced; bgImg.pixelsPerUnitMultiplier = 1.15f;
            bgImg.color = PanelBgColor;
            return go;
        }

        /// <summary>Deux icon-buttons (tri A→Z, optimisation) à droite de la rangée de titre ;
        /// le compteur est décalé pour leur laisser la place, le titre borné (ellipse).</summary>
        private static (Button sort, Button optimize) AddTitleRowButtons(Transform row, Sprite card,
            Sprite sortIcon, Sprite stackIcon, TMP_Text countText) {
            // Compteur décalé à gauche des deux boutons (28px + 6 d'écart chacun).
            var countRt = (RectTransform)countText.transform;
            countRt.offsetMax = new Vector2(-72, countRt.offsetMax.y);

            // Titre : jamais de chevauchement avec compteur/boutons — borne + ellipse.
            Transform titleTr = row.Find("Title");
            if (titleTr != null) {
                var titleRt = (RectTransform)titleTr;
                titleRt.offsetMax = new Vector2(-160, titleRt.offsetMax.y);
                var titleTmp = titleTr.GetComponent<TMP_Text>();
                if (titleTmp != null) titleTmp.overflowMode = TextOverflowModes.Ellipsis;
            }

            Button sort = IconButton(row, "Sort Button", card, sortIcon, new Vector2(-34, 0));
            Button opt  = IconButton(row, "Optimize Button", card, stackIcon, new Vector2(0, 0));
            return (sort, opt);
        }

        private static Button IconButton(Transform row, string name, Sprite card, Sprite icon, Vector2 pos) {
            GameObject go = NewUi(name, row);
            SetRect(go, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), pos, new Vector2(28, 28));
            Image bg = go.AddComponent<Image>();
            bg.sprite = card; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 4f;
            bg.color = new Color(1f, 1f, 1f, 0.10f);
            Button b = go.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            b.colors = cb;

            GameObject ic = NewUi("Icon", go.transform);
            Stretch(ic);
            var icRt = (RectTransform)ic.transform;
            icRt.offsetMin = new Vector2(5, 5); icRt.offsetMax = new Vector2(-5, -5);
            Image icImg = ic.AddComponent<Image>();
            icImg.sprite = icon;
            icImg.color = new Color(0.86f, 0.89f, 0.93f, 0.95f);
            icImg.preserveAspect = true;
            icImg.raycastTarget = false;
            return b;
        }

        // ── Icônes générées (blanc sur transparent, 64×64) ───────────────────────

        private static Sprite EnsureGeneratedIcon(string name, System.Action<Texture2D> draw) {
            string path = $"Assets/Resources/Sprites/Inventory/Generated/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var clear = new Color32(0, 0, 0, 0);
            var px = new Color32[64 * 64];
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            tex.SetPixels32(px);
            draw(tex);
            tex.Apply();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void FillRect(Texture2D t, int x, int y, int w, int h) {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++)
                    if (i >= 0 && i < t.width && j >= 0 && j < t.height) t.SetPixel(i, j, Color.white);
        }

        /// <summary>Tri : trois barres horizontales décroissantes (glyphe « sort »).</summary>
        private static void DrawSortIcon(Texture2D t) {
            FillRect(t, 8, 44, 48, 8);
            FillRect(t, 8, 28, 32, 8);
            FillRect(t, 8, 12, 18, 8);
        }

        /// <summary>Optimisation : deux carrés superposés (piles fusionnées).</summary>
        private static void DrawStackIcon(Texture2D t) {
            // Carré arrière (contour épais).
            FillRect(t, 8, 20, 36, 36);
            // Évide le centre pour un contour de 6px.
            for (int j = 26; j < 50; j++)
                for (int i = 14; i < 38; i++) t.SetPixel(i, j, new Color(0, 0, 0, 0));
            // Carré avant plein, chevauchant en bas-droite.
            FillRect(t, 28, 8, 28, 28);
        }

        /// <summary>Grille de slots dans une zone scrollable verticale : Scroll (ScrollRect +
        /// LayoutElement dimensionné au runtime, plafond 3 rangées) → Viewport (RectMask2D)
        /// → Grid (GridLayoutGroup + ContentSizeFitter) ; scrollbar fine à droite (AutoHide).
        /// Le drag d'item n'entre pas en conflit : DraggableItem consomme OnBeginDrag, le
        /// ScrollRect ne garde que la molette/scrollbar.</summary>
        private static (GameObject scroll, GameObject grid, LayoutElement le) BuildScrollableGrid(
            Transform parent, string scrollName, string gridName,
            int cellSize, int spacing, int pad, int columns, Sprite card) {
            GameObject scrollGo = NewUi(scrollName, parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            float defaultH = 2 * cellSize + spacing + 2 * pad; // 2 rangées avant le 1er snapshot
            le.minHeight = defaultH; le.preferredHeight = defaultH;
            var scroll = scrollGo.AddComponent<ScrollRect>();

            GameObject viewport = NewUi("Viewport", scrollGo.transform);
            Stretch(viewport);
            viewport.AddComponent<RectMask2D>();
            Image vpImg = viewport.AddComponent<Image>();      // cible de raycast pour la molette
            vpImg.color = new Color(0, 0, 0, 0.001f);

            GameObject gridGo = NewUi(gridName, viewport.transform);
            var gridRt = (RectTransform)gridGo.transform;
            gridRt.anchorMin = new Vector2(0, 1); gridRt.anchorMax = new Vector2(1, 1);
            gridRt.pivot = new Vector2(0.5f, 1); gridRt.sizeDelta = new Vector2(0, 0);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellSize, cellSize);
            grid.spacing = new Vector2(spacing, spacing);
            grid.padding = new RectOffset(pad, pad, pad, pad);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            var fit = gridGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Scrollbar fine, collée au bord droit, masquée quand tout tient.
            GameObject sbGo = NewUi("Scrollbar", scrollGo.transform);
            var sbRt = (RectTransform)sbGo.transform;
            sbRt.anchorMin = new Vector2(1, 0); sbRt.anchorMax = Vector2.one;
            sbRt.pivot = new Vector2(1, 0.5f);
            sbRt.offsetMin = new Vector2(-6, 4); sbRt.offsetMax = new Vector2(-1, -4);
            Image sbBg = sbGo.AddComponent<Image>();
            sbBg.sprite = card; sbBg.type = Image.Type.Sliced; sbBg.pixelsPerUnitMultiplier = 8f;
            sbBg.color = new Color(1f, 1f, 1f, 0.06f);
            var sb = sbGo.AddComponent<Scrollbar>();
            GameObject handleGo = NewUi("Handle", sbGo.transform);
            Stretch(handleGo);
            Image handleImg = handleGo.AddComponent<Image>();
            handleImg.sprite = card; handleImg.type = Image.Type.Sliced; handleImg.pixelsPerUnitMultiplier = 8f;
            handleImg.color = new Color(0.70f, 0.74f, 0.80f, 0.55f);
            sb.handleRect = (RectTransform)handleGo.transform;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.transition = Selectable.Transition.None;

            scroll.content = gridRt;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return (scrollGo, gridGo, le);
        }

        /// <summary>Fine ligne de séparation sous la rangée de titre (maquette).</summary>
        private static void BuildSeparator(Transform parent) {
            GameObject sep = NewUi("Separator", parent);
            var le = sep.AddComponent<LayoutElement>();
            le.minHeight = 2; le.preferredHeight = 2;
            var img = sep.AddComponent<Image>();
            img.color = SeparatorColor;
            img.raycastTarget = false;
        }

        /// <summary>Rangée de titre : libellé à gauche (brun, UpperCase) + compteur à droite.</summary>
        private static GameObject BuildTitleRow(Transform parent, TMP_FontAsset bold, string title,
            out TMP_Text titleText, out TMP_Text countText) {
            GameObject row = NewUi("Title Row", parent);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 34; le.preferredHeight = 34;
            titleText = TextStretch(row.transform, "Title", title, bold, 20, PanelTitleColor,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold | FontStyles.UpperCase);
            countText = TextStretch(row.transform, "Count", "", bold, 16, SoftTextColor,
                TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            return row;
        }

        /// <summary>Clone le template de slot poche existant (Image Card + ItemSlot + outlines
        /// sélection + Drop Highlight), en ajustant le filtre pocketable.</summary>
        private static ItemSlot CloneSlotTemplate(Transform source, Transform parent, string name,
            bool rejectsNonPocketable) {
            GameObject clone = Object.Instantiate(source.gameObject, parent);
            clone.name = name;
            clone.SetActive(false);
            var slot = clone.GetComponent<ItemSlot>();
            var so = new SerializedObject(slot);
            so.FindProperty("rejectsNonPocketable").boolValue = rejectsNonPocketable;
            so.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        private static TMP_Text Text(Transform parent, string name, string value, TMP_FontAsset font,
            float size, Color color, TextAlignmentOptions align, Vector2 pos, Vector2 sizeDelta, FontStyles style) {
            GameObject go = NewUi(name, parent);
            SetRect(go, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, sizeDelta);
            return FillText(go, value, font, size, color, align, style);
        }

        private static TMP_Text TextStretch(Transform parent, string name, string value, TMP_FontAsset font,
            float size, Color color, TextAlignmentOptions align, FontStyles style) {
            GameObject go = NewUi(name, parent);
            Stretch(go);
            return FillText(go, value, font, size, color, align, style);
        }

        /// <summary>Texte géré par un layout group (pas d'anchors manuels).</summary>
        private static TMP_Text TextLayout(Transform parent, string name, string value, TMP_FontAsset font,
            float size, Color color, TextAlignmentOptions align, FontStyles style) {
            GameObject go = NewUi(name, parent);
            return FillText(go, value, font, size, color, align, style);
        }

        private static TMP_Text FillText(GameObject go, string value, TMP_FontAsset font, float size,
            Color color, TextAlignmentOptions align, FontStyles style) {
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = value;
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static TMP_FontAsset FindFont(string name) {
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset " + name.Split(' ')[0])) {
                var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (f != null && f.name == name) return f;
            }
            return null;
        }
    }
}
