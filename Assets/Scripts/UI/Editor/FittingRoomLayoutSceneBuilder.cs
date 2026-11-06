using System.Collections.Generic;
using Sim.Scriptables;
using Sim.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SimEditor {
    /// <summary>
    /// Builds a standalone LAYOUT scene (<c>Assets/Scenes/UI Layout.unity</c>) to preview the
    /// fitting-room HUD without entering play mode: studio camera (solid background, Player
    /// layer only — same override as the real fitting view), placeholder mannequin, and a
    /// populated clone of the "FittingRoomUI (Built)" panel from the HUD Manager prefab
    /// (tabs, article cards from the real clothing catalog, detail panel, cart totals).
    ///
    /// The clone is a DUMMY: the FittingRoomUI component is stripped so entering play mode in
    /// this scene never hijacks the singleton or hides the panel. Re-run the menu after
    /// « Build Fitting Room UI » to refresh the preview from the rebuilt prefab.
    /// </summary>
    public static class FittingRoomLayoutSceneBuilder {
        private const string ScenePath      = "Assets/Scenes/UI Layout.unity";
        private const string HudPrefabPath  = "Assets/Resources/Prefabs/Managers/HUD Manager.prefab";
        private const string CatalogPath    = "Assets/Resources/Configurations/Shops/ClothingShopCatalog.asset";

        private const int PreviewTab   = 2;  // « Hauts »
        private const int MaxCards     = 30;
        private static readonly Color StudioBackground = new Color(0.13f, 0.12f, 0.11f, 1f);
        private static readonly Color TabActiveColor   = new Color(0.22f, 0.42f, 0.65f, 1f);
        private static readonly Color TabInactiveColor = new Color(0.16f, 0.17f, 0.21f, 1f);
        private static readonly Color CardSelectedColor = new Color(0.22f, 0.32f, 0.46f, 1f);

        private static readonly string[] TabNames = { "Chapeaux", "Accessoires", "Hauts", "Pantalons", "Chaussures" };

        [MenuItem("Tools/The BROZ/Build UI Layout Scene (Fitting Room)")]
        public static void Build() {
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            FittingRoomUI source = hudPrefab != null ? hudPrefab.GetComponentInChildren<FittingRoomUI>(true) : null;
            if (source == null) {
                Debug.LogError($"[FittingRoomLayoutSceneBuilder] No FittingRoomUI found in {HudPrefabPath} — run Tools ▸ The BROZ ▸ Build Fitting Room UI first.");
                return;
            }

            // Built ADDITIVELY so the currently open scene (and its unsaved changes) is never
            // touched: new empty scene → build → save → close, active scene restored.
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try {
                BuildStudio();
                Transform canvas = BuildCanvas();
                BuildPanel(source, canvas);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log($"[FittingRoomLayoutSceneBuilder] Built layout scene at {ScenePath} — open it to preview the fitting-room HUD in the Game view.");
        }

        // ── Studio (camera + mannequin), same rendering as the real fitting view ──────

        private static void BuildStudio() {
            GameObject camGo = new GameObject("Studio Camera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = StudioBackground;
            // Player (mannequin) + UI (the ScreenSpaceCamera canvas — the real HUD is Overlay
            // and bypasses culling, but here the UI goes through this camera).
            cam.cullingMask = LayerMask.GetMask("Player", "UI");
            // Render last when the layout scene is loaded next to another scene: the solid
            // clear covers that scene's cameras in the Game view.
            cam.depth = 90f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 1.35f, 2.6f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, 0f) - camGo.transform.position);

            GameObject lightGo = new GameObject("Studio Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(40f, 200f, 0f);

            // CreatePrimitive assigns the BUILT-IN default material (magenta under URP) —
            // give the mannequin an explicit URP Lit gray (scene-owned instance).
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Material defaultMat = lit != null ? new Material(lit) { color = new Color(0.6f, 0.6f, 0.62f, 1f) } : null;

            int playerLayer = LayerMask.NameToLayer("Player");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Mannequin";
            body.transform.position = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            if (playerLayer >= 0) body.layer = playerLayer;
            if (defaultMat != null) body.GetComponent<Renderer>().sharedMaterial = defaultMat;

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(body.transform, true);
            head.transform.position = new Vector3(0f, 1.95f, 0f);
            head.transform.localScale = Vector3.one * 0.6f; // scaled by the capsule's transform
            Object.DestroyImmediate(head.GetComponent<Collider>());
            if (playerLayer >= 0) head.layer = playerLayer;
            if (defaultMat != null) head.GetComponent<Renderer>().sharedMaterial = defaultMat;
        }

        private static Transform BuildCanvas() {
            GameObject canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            // ScreenSpaceCamera (the real HUD canvas is Overlay): same metrics with the scaler,
            // but the UI goes through the studio camera so it shows up in captured render
            // textures / screenshot tooling, not only in the live Game view.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                is { Length: > 0 } cams ? System.Array.Find(cams, c => c.name == "Studio Camera") : null;
            canvas.planeDistance = 1f;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return canvasGo.transform;
        }

        // ── Panel clone + dummy population ────────────────────────────────────────────

        private static void BuildPanel(FittingRoomUI source, Transform canvas) {
            GameObject panel = Object.Instantiate(source.gameObject, canvas, false);
            panel.name = "FittingRoomUI (Layout)";
            panel.SetActive(true);

            // Grab the builder-wired refs (remapped to the clone's own children), then strip
            // the component so play mode in this scene never touches the real singleton.
            FittingRoomUI ui = panel.GetComponent<FittingRoomUI>();
            SerializedObject so = new SerializedObject(ui);
            TMP_Text title        = so.FindProperty("titleLabel").objectReferenceValue as TMP_Text;
            TMP_Text money        = so.FindProperty("moneyLabel").objectReferenceValue as TMP_Text;
            Transform tabs        = so.FindProperty("tabsContainer").objectReferenceValue as Transform;
            Button tabTemplate    = so.FindProperty("tabTemplate").objectReferenceValue as Button;
            Transform grid        = so.FindProperty("gridContainer").objectReferenceValue as Transform;
            FittingRoomCard cardTemplate = so.FindProperty("cardTemplate").objectReferenceValue as FittingRoomCard;
            GameObject detailRoot = so.FindProperty("detailRoot").objectReferenceValue as GameObject;
            Image detailIcon      = so.FindProperty("detailIcon").objectReferenceValue as Image;
            TMP_Text detailName   = so.FindProperty("detailName").objectReferenceValue as TMP_Text;
            TMP_Text detailPrice  = so.FindProperty("detailPrice").objectReferenceValue as TMP_Text;
            TMP_Text detailDesc   = so.FindProperty("detailDescription").objectReferenceValue as TMP_Text;
            TMP_Text detailStats  = so.FindProperty("detailStats").objectReferenceValue as TMP_Text;
            TMP_Text tryLabel     = so.FindProperty("tryButtonLabel").objectReferenceValue as TMP_Text;
            TMP_Text total        = so.FindProperty("totalLabel").objectReferenceValue as TMP_Text;
            TMP_Text buyLabel     = so.FindProperty("buyButtonLabel").objectReferenceValue as TMP_Text;
            Object.DestroyImmediate(ui);

            // Catalog entries for the previewed tab, like FittingRoomUI.BuildEntries (no gender
            // filter — this is a layout, duplicates don't matter).
            ClothingShopCatalogConfig catalog = AssetDatabase.LoadAssetAtPath<ClothingShopCatalogConfig>(CatalogPath);
            var entries = new List<(ClothingConfig cfg, int price)>();
            Sprite[] slotIcons = new Sprite[TabNames.Length];
            if (catalog != null) {
                foreach (ItemPrice entry in catalog.Entries) {
                    if (entry.item is not ClothingConfig clothing) continue;
                    int slot = clothing.WearSlotIndex;
                    if (slot >= 0 && slot < slotIcons.Length && slotIcons[slot] == null) slotIcons[slot] = clothing.Icon;
                    if (slot == PreviewTab) entries.Add((clothing, entry.price));
                }
                entries.Sort((a, b) => string.Compare(a.cfg.Label, b.cfg.Label, System.StringComparison.Ordinal));
            }

            // Tabs.
            if (tabTemplate != null && tabs != null) {
                for (int i = 0; i < TabNames.Length; i++) {
                    Button tab = Object.Instantiate(tabTemplate, tabs);
                    tab.gameObject.SetActive(true);
                    TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
                    if (label != null) label.text = TabNames[i];
                    Transform iconT = tab.transform.Find("Icon");
                    Image tabIcon = iconT != null ? iconT.GetComponent<Image>() : null;
                    if (tabIcon != null) {
                        tabIcon.sprite = slotIcons[i];
                        tabIcon.enabled = slotIcons[i] != null;
                    }
                    Image bg = tab.GetComponent<Image>();
                    if (bg != null) bg.color = i == PreviewTab ? TabActiveColor : TabInactiveColor;
                }
            }

            // Cards: first one IN CART (checkmark), second one SELECTED (highlight + detail).
            (ClothingConfig cfg, int price) inCart = default;
            (ClothingConfig cfg, int price) selected = default;
            if (cardTemplate != null && grid != null) {
                for (int i = 0; i < entries.Count && i < MaxCards; i++) {
                    FittingRoomCard card = Object.Instantiate(cardTemplate, grid);
                    card.gameObject.SetActive(true);

                    SerializedObject cardSo = new SerializedObject(card);
                    Image icon = cardSo.FindProperty("icon").objectReferenceValue as Image;
                    TMP_Text price = cardSo.FindProperty("priceLabel").objectReferenceValue as TMP_Text;
                    GameObject check = cardSo.FindProperty("checkmark").objectReferenceValue as GameObject;
                    Image background = cardSo.FindProperty("background").objectReferenceValue as Image;

                    if (icon != null) {
                        icon.sprite = entries[i].cfg.Icon;
                        icon.enabled = entries[i].cfg.Icon != null;
                    }
                    if (price != null) price.text = $"{entries[i].price} $";
                    if (i == 0) {
                        if (check != null) check.SetActive(true);
                        inCart = entries[i];
                    }
                    if (i == 1) {
                        if (background != null) background.color = CardSelectedColor;
                        selected = entries[i];
                    }
                }
            }
            if (selected.cfg == null) selected = inCart;

            // Detail panel filled with the selected article.
            if (detailRoot != null) detailRoot.SetActive(selected.cfg != null);
            if (selected.cfg != null) {
                if (detailIcon != null) {
                    detailIcon.sprite = selected.cfg.Icon;
                    detailIcon.enabled = selected.cfg.Icon != null;
                }
                if (detailName != null) detailName.text = selected.cfg.Label;
                if (detailPrice != null) detailPrice.text = $"{selected.price} $";
                if (detailDesc != null) {
                    detailDesc.text = string.IsNullOrEmpty(selected.cfg.Description)
                        ? "Description de l'article…" : selected.cfg.Description;
                }
                if (detailStats != null) detailStats.text = FittingRoomUI.StatsText(selected.cfg);
                if (tryLabel != null) tryLabel.text = "Essayer";
            }

            // Header + cart totals.
            if (title != null && catalog != null) title.text = catalog.ShopLabel;
            if (money != null) money.text = "250 $";
            int cartTotal = inCart.cfg != null ? inCart.price : 0;
            if (total != null) total.text = $"Total : {cartTotal} $";
            if (buyLabel != null) buyLabel.text = inCart.cfg != null ? "Acheter (1)" : "Acheter";

            // The whole tree on the UI layer so the studio camera's culling mask renders it
            // (the builder authored the prefab panel on the Default layer).
            SetLayerRecursively(canvas.gameObject, LayerMask.NameToLayer("UI"));
        }

        private static void SetLayerRecursively(GameObject go, int layer) {
            if (layer < 0) return;
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
