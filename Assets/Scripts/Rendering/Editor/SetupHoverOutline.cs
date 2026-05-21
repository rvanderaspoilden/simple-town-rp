using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SimEditor {
    /// <summary>
    /// One-click setup for the hover outline (no MCP / no manual inspector wiring):
    ///   1. Ensures an "Outline" layer exists.
    ///   2. Adds + configures an OutlineRendererFeature on every URP Renderer
    ///      (UniversalRendererData) that doesn't already have one.
    ///
    /// Run "Tools/The BROZ/Setup Hover Outline". Idempotent.
    /// </summary>
    public static class SetupHoverOutline {
        private const string LayerName = "Outline";

        [MenuItem("Tools/The BROZ/Setup Hover Outline")]
        public static void Setup() {
            int layer = EnsureLayer(LayerName);
            if (layer < 0) {
                EditorUtility.DisplayDialog("Setup Hover Outline",
                    "Could not create the 'Outline' layer (no free user-layer slot). Free a layer and retry.", "OK");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
            int added = 0, already = 0;

            foreach (string g in guids) {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;

                if (data.rendererFeatures.Any(f => f is OutlineRendererFeature)) {
                    already++;
                    continue;
                }

                var feature = ScriptableObject.CreateInstance<OutlineRendererFeature>();
                feature.name = "Outline (Hover)";
                feature.hideFlags = HideFlags.HideInHierarchy;
                feature.settings.outlineLayer = 1 << layer;

                AssetDatabase.AddObjectToAsset(feature, data);

                var so = new SerializedObject(data);
                var featuresProp = so.FindProperty("m_RendererFeatures");
                var mapProp      = so.FindProperty("m_RendererFeatureMap");
                int idx = featuresProp.arraySize;
                featuresProp.InsertArrayElementAtIndex(idx);
                featuresProp.GetArrayElementAtIndex(idx).objectReferenceValue = feature;
                mapProp.InsertArrayElementAtIndex(idx);
                mapProp.GetArrayElementAtIndex(idx).longValue = feature.GetInstanceID();
                so.ApplyModifiedProperties();

                EditorUtility.SetDirty(data);
                added++;
                Debug.Log($"[SetupHoverOutline] Added OutlineRendererFeature to {path}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Setup Hover Outline",
                $"Outline layer = index {layer}.\nAdded the feature to {added} renderer(s) ({already} already had it).\n\nPlay and hover a prop.",
                "OK");
        }

        /// <summary>Returns the index of the layer, creating it in the first free user slot if needed.</summary>
        private static int EnsureLayer(string name) {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;
            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null) return -1;

            // User layers are 8..31; 0..7 are reserved by Unity.
            for (int i = 8; i < layers.arraySize; i++) {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue)) {
                    slot.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"[SetupHoverOutline] Created layer '{name}' at index {i}");
                    return i;
                }
            }
            return -1;
        }
    }
}
