using System.IO;
using Sim.Scriptables;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimEditor {
    /// <summary>
    /// Generates a default, customizable billboard prefab (world-space canvas + label
    /// + PropSaleBillboard component, all wired) and assigns it to SaleActionsConfig's
    /// billboardPrefab field. Run "Tools/The BROZ/Build Sale Billboard Prefab".
    /// Re-running overwrites the prefab. After generation, edit the prefab freely.
    /// </summary>
    public static class SaleBillboardPrefabBuilder {
        private const string Folder        = "Assets/Resources/Configurations/Sale";
        private const string PrefabPath    = Folder + "/SaleBillboard.prefab";
        private const string ConfigPath    = Folder + "/SaleActionsConfig.asset";

        [MenuItem("Tools/The BROZ/Build Sale Billboard Prefab")]
        public static void Build() {
            EnsureFolder(Folder);

            // ── Build the hierarchy in the scene, then save as a prefab. ──────────
            var root = new GameObject("SaleBillboard");
            var billboard = root.AddComponent<PropSaleBillboard>();

            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(root.transform, false);
            var visualRoot = (RectTransform)canvasGo.transform;
            var canvasGroup = canvasGo.GetComponent<CanvasGroup>();
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            visualRoot.sizeDelta  = new Vector2(240, 90);
            visualRoot.localScale = Vector3.one * 0.0075f;

            var bgGo = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGo.transform.SetParent(visualRoot, false);
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bgGo.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.85f);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(visualRoot, false);
            var lblRt = (RectTransform)labelGo.transform;
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = new Vector2(8, 6); lblRt.offsetMax = new Vector2(-8, -6);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize  = 28;
            label.color     = Color.white;
            label.text      = "À VENDRE";

            // Wire the component's serialized refs.
            SerializedObject so = new SerializedObject(billboard);
            so.FindProperty("visualRoot").objectReferenceValue  = visualRoot;
            so.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
            so.FindProperty("label").objectReferenceValue       = label;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            if (prefab == null) {
                EditorUtility.DisplayDialog("Build Sale Billboard Prefab", "Failed to save the prefab.", "OK");
                return;
            }

            // ── Assign onto SaleActionsConfig (create it if missing). ─────────────
            SaleActionsConfig config = AssetDatabase.LoadAssetAtPath<SaleActionsConfig>(ConfigPath);
            if (config == null) {
                config = ScriptableObject.CreateInstance<SaleActionsConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            SerializedObject cso = new SerializedObject(config);
            cso.FindProperty("billboardPrefab").objectReferenceValue = prefab;
            cso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = prefab;
            Debug.Log($"[SaleBillboardPrefabBuilder] Saved {PrefabPath} and assigned it to {ConfigPath}.");
            EditorUtility.DisplayDialog("Build Sale Billboard Prefab",
                $"Created {PrefabPath}\nand assigned it to SaleActionsConfig.\n\nEdit the prefab to customize the look.",
                "OK");
        }

        private static void EnsureFolder(string folder) {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++) {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
