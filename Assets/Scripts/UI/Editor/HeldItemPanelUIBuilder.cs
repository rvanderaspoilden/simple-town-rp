using Sim.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UIEditor {
    /// <summary>
    /// One-clic : construit l'encart « item tenu » (HeldItemPanelUI) dans le prefab
    /// HUD Manager — bas-droite, AU-DESSUS du bout de téléphone visible (le téléphone
    /// ouvert glisse PAR-DESSUS : l'encart est inséré avant lui dans l'ordre des siblings).
    /// Icône + nom + sous-titre type d'objet ; arme à feu → ligne munitions à la place.
    /// Relançable : l'encart existant est reconstruit de zéro à chaque exécution.
    /// Styles clonés de l'existant (Card.png, Montserrat, palette loot overlay).
    /// </summary>
    public static class HeldItemPanelUIBuilder {
        private const string PrefabPath = "Assets/Resources/Prefabs/Managers/HUD Manager.prefab";

        // Palette maquette (mêmes tons que le loot overlay).
        private static readonly Color PanelBgColor   = new Color(0.055f, 0.068f, 0.090f, 0.88f);
        private static readonly Color PanelEdgeColor = new Color(0.80f, 0.84f, 0.90f, 0.25f);
        private static readonly Color SoftTextColor  = new Color(0.72f, 0.75f, 0.80f, 1f);
        private static readonly Color IconFrameColor = new Color(1f, 1f, 1f, 0.07f);

        [MenuItem("Tools/The BROZ/Build Held Item Panel UI")]
        public static void Build() {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try {
                BuildInto(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[HeldItemPanelUIBuilder] Encart item tenu construit → " + PrefabPath);
            }
            finally {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void BuildInto(GameObject prefabRoot) {
            Transform view = prefabRoot.transform.Find("Default View");
            if (view == null) throw new System.Exception("Default View introuvable dans le prefab HUD.");
            Transform phone = view.Find("Phone");
            if (phone == null) throw new System.Exception("Phone introuvable sous Default View.");

            Sprite card = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/Frames/Card.png");
            TMP_FontAsset bold    = FindFont("Montserrat-Bold SDF");
            TMP_FontAsset regular = FindFont("Montserrat-Regular 2 SDF") ?? bold;
            if (card == null || bold == null)
                throw new System.Exception("Style source manquant (Card/Montserrat).");

            // Reconstruit de zéro.
            Transform existing = view.Find("Held Item Panel");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // ── Racine (GO ACTIF porteur du composant ; Root enfant toggle) ──────
            // Insérée À L'INDEX du téléphone : le téléphone (sibling suivant) se rend
            // par-dessus quand il glisse ouvert.
            GameObject panelGo = NewUi("Held Item Panel", view);
            panelGo.transform.SetSiblingIndex(phone.GetSiblingIndex());
            SetRect(panelGo, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-14, 84), new Vector2(330, 78));
            var panel = panelGo.AddComponent<HeldItemPanelUI>();

            GameObject rootGo = NewUi("Root", panelGo.transform);
            Stretch(rootGo);

            // Liseré + fond (aucune cible de raycast : l'encart est purement informatif).
            GameObject border = NewUi("Border", rootGo.transform);
            Stretch(border);
            Image bImg = border.AddComponent<Image>();
            bImg.sprite = card; bImg.type = Image.Type.Sliced; bImg.pixelsPerUnitMultiplier = 1.6f;
            bImg.color = PanelEdgeColor;
            bImg.raycastTarget = false;

            GameObject bg = NewUi("Bg", rootGo.transform);
            Stretch(bg);
            var bgRt = (RectTransform)bg.transform;
            bgRt.offsetMin = new Vector2(2, 2); bgRt.offsetMax = new Vector2(-2, -2);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.sprite = card; bgImg.type = Image.Type.Sliced; bgImg.pixelsPerUnitMultiplier = 1.7f;
            bgImg.color = PanelBgColor;
            bgImg.raycastTarget = false;

            // ── Icône (cadre à gauche) ────────────────────────────────────────────
            GameObject frame = NewUi("Icon Frame", rootGo.transform);
            SetRect(frame, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(11, 0), new Vector2(56, 56));
            Image frameImg = frame.AddComponent<Image>();
            frameImg.sprite = card; frameImg.type = Image.Type.Sliced; frameImg.pixelsPerUnitMultiplier = 3f;
            frameImg.color = IconFrameColor;
            frameImg.raycastTarget = false;

            GameObject icon = NewUi("Icon", frame.transform);
            Stretch(icon);
            var iconRt = (RectTransform)icon.transform;
            iconRt.offsetMin = new Vector2(5, 5); iconRt.offsetMax = new Vector2(-5, -5);
            Image iconImg = icon.AddComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            // ── Nom (moitié haute) ────────────────────────────────────────────────
            GameObject nameGo = NewUi("Name", rootGo.transform);
            var nameRt = (RectTransform)nameGo.transform;
            nameRt.anchorMin = new Vector2(0, 0.5f); nameRt.anchorMax = Vector2.one;
            nameRt.offsetMin = new Vector2(78, -2); nameRt.offsetMax = new Vector2(-14, -8);
            TMP_Text name = FillText(nameGo, "", bold, 18, Color.white,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            name.overflowMode = TextOverflowModes.Ellipsis;

            // ── Sous-titre type d'objet (moitié basse — item non spécifique) ──────
            GameObject subGo = NewUi("Subtitle", rootGo.transform);
            var subRt = (RectTransform)subGo.transform;
            subRt.anchorMin = Vector2.zero; subRt.anchorMax = new Vector2(1, 0.5f);
            subRt.offsetMin = new Vector2(78, 8); subRt.offsetMax = new Vector2(-14, 2);
            TMP_Text subtitle = FillText(subGo, "", regular, 14, SoftTextColor,
                TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            subtitle.overflowMode = TextOverflowModes.Ellipsis;

            // ── Ligne munitions (moitié basse — arme à feu) ───────────────────────
            GameObject ammoRow = NewUi("Ammo Row", rootGo.transform);
            var ammoRowRt = (RectTransform)ammoRow.transform;
            ammoRowRt.anchorMin = Vector2.zero; ammoRowRt.anchorMax = new Vector2(1, 0.5f);
            ammoRowRt.offsetMin = new Vector2(78, 6); ammoRowRt.offsetMax = new Vector2(-14, 2);

            Sprite bullets = EnsureGeneratedIcon("icon_bullets", DrawBulletsIcon);
            GameObject bIcon = NewUi("Bullets Icon", ammoRow.transform);
            SetRect(bIcon, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, 0), new Vector2(20, 16));
            Image bIconImg = bIcon.AddComponent<Image>();
            bIconImg.sprite = bullets;
            bIconImg.color = SoftTextColor;
            bIconImg.preserveAspect = true;
            bIconImg.raycastTarget = false;

            GameObject ammoGo = NewUi("Ammo", ammoRow.transform);
            var ammoRt = (RectTransform)ammoGo.transform;
            ammoRt.anchorMin = Vector2.zero; ammoRt.anchorMax = Vector2.one;
            ammoRt.offsetMin = new Vector2(28, 0); ammoRt.offsetMax = Vector2.zero;
            TMP_Text ammo = FillText(ammoGo, "", bold, 18, Color.white,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // ── Câblage du composant ─────────────────────────────────────────────
            var so = new SerializedObject(panel);
            so.FindProperty("root").objectReferenceValue         = rootGo;
            so.FindProperty("iconImage").objectReferenceValue    = iconImg;
            so.FindProperty("nameText").objectReferenceValue     = name;
            so.FindProperty("subtitleText").objectReferenceValue = subtitle;
            so.FindProperty("ammoRow").objectReferenceValue      = ammoRow;
            so.FindProperty("ammoText").objectReferenceValue     = ammo;
            so.ApplyModifiedPropertiesWithoutUndo();

            rootGo.SetActive(false);
        }

        // ── Helpers (mêmes patterns que LootOverlayUIBuilder) ────────────────────

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

        /// <summary>Munitions : trois cartouches verticales (corps + ogive plus étroite).</summary>
        private static void DrawBulletsIcon(Texture2D t) {
            for (int k = 0; k < 3; k++) {
                int x = 8 + k * 18;
                FillRect(t, x, 10, 12, 34);     // corps
                FillRect(t, x + 2, 44, 8, 8);   // épaule
                FillRect(t, x + 4, 52, 4, 4);   // ogive
            }
        }
    }
}
