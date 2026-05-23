using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sim.Building;     // PropsRenderer
using Sim.Scriptables;  // PropsConfig
using UnityEditor;
using UnityEngine;

namespace SimEditor {
    /// <summary>
    /// One-shot maintenance command that brings every prop prefab under
    /// Assets/Resources/Prefabs/Props up to the standard "new system" setup, modelled
    /// on the Fridge prefab:
    ///   - PropIdentity        (roomId defaults to "city")
    ///   - GenericPropSource   (server-side state source)
    ///   - GenericPropBehaviour (client behaviour)  — only when the prefab has no
    ///                          PropBehaviourBase yet (specialised doors/seats/etc. are left intact)
    ///   - PropsRenderer       (renderersToModify auto-filled with the prefab's Mesh/Skinned renderers)
    ///
    /// The two-way config link (GenericPropBehaviour.configuration ↔ PropsConfig.prefab)
    /// is set ONLY when a prefab maps to exactly one PropsConfig by normalised name AND
    /// that config is claimed by exactly one prefab (1:1). Ambiguous / unmatched prefabs
    /// get the components but keep an empty configuration — they're listed in the report
    /// for the designer to finalise by hand.
    ///
    /// Idempotent: re-running only adds what's missing and never overwrites an existing
    /// configuration link. Run "Tools/The BROZ/Configure Prop Prefabs".
    /// </summary>
    public static class PropPrefabConfigurator {
        private const string PropsPrefabFolder = "Assets/Resources/Prefabs/Props";

        [MenuItem("Tools/The BROZ/Configure Prop Prefabs")]
        public static void Configure() {
            // ── Index configs by normalised name (asset name + displayName) ──────────
            PropsConfig[] configs = LoadAllConfigs();
            Dictionary<string, List<PropsConfig>> configByName = BuildConfigNameIndex(configs);

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PropsPrefabFolder });

            // ── Pass 1: resolve a confident 1:1 prefab ↔ config mapping ──────────────
            // prefabPath → unique config (or null when 0/▸1 candidates).
            var prefabPaths      = prefabGuids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var candidateByPath  = new Dictionary<string, PropsConfig>();
            var prefabsPerConfig = new Dictionary<PropsConfig, int>();

            foreach (string path in prefabPaths) {
                string norm = Normalize(System.IO.Path.GetFileNameWithoutExtension(path));
                PropsConfig unique = null;
                if (configByName.TryGetValue(norm, out var matches) && matches.Count == 1)
                    unique = matches[0];
                candidateByPath[path] = unique;
                if (unique != null)
                    prefabsPerConfig[unique] = prefabsPerConfig.TryGetValue(unique, out int n) ? n + 1 : 1;
            }

            // ── Pass 2: configure each prefab ────────────────────────────────────────
            var configured = new List<string>();   // components + config link set
            var ambiguous  = new List<string>();    // components added, configuration left empty
            var specialised = new List<string>();   // already had a non-generic behaviour
            var noCollider = new List<string>();     // won't be interactable until a collider is added

            foreach (string path in prefabPaths) {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try {
                    PropBehaviourBase behaviour = root.GetComponent<PropBehaviourBase>();
                    bool wasSpecialised = behaviour != null && !(behaviour is GenericPropBehaviour);

                    if (behaviour == null) {
                        EnsureComponent<PropIdentity>(root);
                        EnsureComponent<GenericPropSource>(root);
                        behaviour = root.AddComponent<GenericPropBehaviour>();
                    } else {
                        EnsureComponent<PropIdentity>(root);
                        // Specialised prefabs already have their own ServerPropSource; don't touch it.
                        if (!wasSpecialised) EnsureComponent<GenericPropSource>(root);
                    }

                    PropsRenderer renderer = EnsureComponent<PropsRenderer>(root);
                    WireRenderersIfEmpty(renderer, root);

                    // Config link only when confident 1:1 and not already set.
                    PropsConfig matched = behaviour.GetConfiguration();
                    if (matched == null) {
                        PropsConfig cand = candidateByPath[path];
                        if (cand != null && prefabsPerConfig.TryGetValue(cand, out int count) && count == 1) {
                            matched = cand;
                            behaviour.SetConfiguration(matched);
                        }
                    }

                    bool hasCollider = root.GetComponentInChildren<Collider>(true) != null;

                    PrefabUtility.SaveAsPrefabAsset(root, path);

                    // Reverse link config.prefab → this prefab's persistent behaviour component.
                    if (matched != null) LinkConfigToPrefab(matched, path);

                    string name = root.name;
                    if (wasSpecialised) specialised.Add($"{name} ({behaviour.GetType().Name})");
                    else if (matched != null) configured.Add($"{name} → {matched.name}");
                    else ambiguous.Add(name);
                    if (!hasCollider) noCollider.Add(name);
                } finally {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LogReport(prefabPaths.Length, configured, ambiguous, specialised, noCollider);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private static T EnsureComponent<T>(GameObject go) where T : Component {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void WireRenderersIfEmpty(PropsRenderer renderer, GameObject root) {
            SerializedObject so = new SerializedObject(renderer);
            SerializedProperty arr = so.FindProperty("renderersToModify");
            if (arr == null || arr.arraySize > 0) return;

            Renderer[] rends = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                .ToArray();
            if (rends.Length == 0) return;

            arr.arraySize = rends.Length;
            for (int i = 0; i < rends.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = rends[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void LinkConfigToPrefab(PropsConfig config, string prefabPath) {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            PropBehaviourBase behaviour = asset != null ? asset.GetComponent<PropBehaviourBase>() : null;
            if (behaviour == null) return;

            SerializedObject so = new SerializedObject(config);
            SerializedProperty prop = so.FindProperty("prefab");
            if (prop != null && prop.objectReferenceValue != behaviour) {
                prop.objectReferenceValue = behaviour;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
            }
        }

        private static PropsConfig[] LoadAllConfigs() =>
            AssetDatabase.FindAssets("t:PropsConfig")
                .Select(g => AssetDatabase.LoadAssetAtPath<PropsConfig>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null)
                .ToArray();

        private static Dictionary<string, List<PropsConfig>> BuildConfigNameIndex(PropsConfig[] configs) {
            var index = new Dictionary<string, List<PropsConfig>>();
            foreach (PropsConfig c in configs) {
                Add(index, Normalize(c.name), c);
                string display = c.GetDisplayName();
                if (!string.IsNullOrEmpty(display)) Add(index, Normalize(display), c);
            }
            return index;
        }

        private static void Add(Dictionary<string, List<PropsConfig>> index, string key, PropsConfig c) {
            if (string.IsNullOrEmpty(key)) return;
            if (!index.TryGetValue(key, out var list)) { list = new List<PropsConfig>(); index[key] = list; }
            if (!list.Contains(c)) list.Add(c);
        }

        /// <summary>Lowercase, alphanumeric only — tolerant of spaces / underscores / casing.</summary>
        private static string Normalize(string s) {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        private static void LogReport(int total, List<string> configured, List<string> ambiguous,
                                      List<string> specialised, List<string> noCollider) {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[PropPrefabConfigurator] Scanned {total} prefab(s) under {PropsPrefabFolder}.");
            sb.AppendLine($"  ✓ Configured (components + config link) : {configured.Count}");
            sb.AppendLine($"  ⚠ Components added, configuration EMPTY  : {ambiguous.Count}  (à lier à la main)");
            sb.AppendLine($"  ↷ Specialised behaviour left intact      : {specialised.Count}");
            sb.AppendLine($"  ⛔ No collider (non interactif tel quel)  : {noCollider.Count}");

            if (ambiguous.Count > 0)
                sb.AppendLine("\n— configuration À LIER manuellement —\n  " + string.Join("\n  ", ambiguous));
            if (noCollider.Count > 0)
                sb.AppendLine("\n— prefabs SANS collider —\n  " + string.Join("\n  ", noCollider));
            if (specialised.Count > 0)
                sb.AppendLine("\n— behaviours spécialisés (inchangés) —\n  " + string.Join("\n  ", specialised));
            if (configured.Count > 0)
                sb.AppendLine("\n— liés automatiquement —\n  " + string.Join("\n  ", configured));

            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("Configure Prop Prefabs",
                $"Scanné {total} prefab(s).\n\n" +
                $"✓ Configurés (composants + config) : {configured.Count}\n" +
                $"⚠ Composants ajoutés, config vide  : {ambiguous.Count}\n" +
                $"↷ Spécialisés (inchangés)          : {specialised.Count}\n" +
                $"⛔ Sans collider                    : {noCollider.Count}\n\n" +
                "Détails dans la Console.", "OK");
        }
    }
}
