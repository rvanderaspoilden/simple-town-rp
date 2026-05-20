using Sim.Scriptables;
using UnityEditor;
using UnityEngine;

namespace SimEditor {
    /// <summary>
    /// One-shot data migration before the legacy PropsConfig.buyable flag is removed:
    /// every PropsConfig with buyable == true also gets sellable = true, so the
    /// consolidated "sellable" flag preserves which props appear in the shop.
    ///
    /// Run "Tools/The BROZ/Migrate buyable -> sellable" BEFORE the buyable field is
    /// deleted from PropsConfig (this reads it via SerializedProperty). Idempotent.
    /// </summary>
    public static class MigrateBuyableToSellableTool {
        [MenuItem("Tools/The BROZ/Migrate buyable -> sellable")]
        public static void Migrate() {
            string[] guids = AssetDatabase.FindAssets("t:PropsConfig");
            int changed = 0;
            int alreadyOk = 0;
            int total = guids.Length;

            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PropsConfig config = AssetDatabase.LoadAssetAtPath<PropsConfig>(path);
                if (config == null) continue;

                SerializedObject so = new SerializedObject(config);
                SerializedProperty buyable  = so.FindProperty("buyable");
                SerializedProperty sellable = so.FindProperty("sellable");

                if (buyable == null) {
                    Debug.LogWarning($"[Migrate] '{config.name}' has no 'buyable' field (already removed?) — {path}");
                    continue;
                }
                if (sellable == null) {
                    Debug.LogError($"[Migrate] '{config.name}' has no 'sellable' field — make sure PropsConfig still declares it. {path}");
                    continue;
                }

                if (buyable.boolValue && !sellable.boolValue) {
                    sellable.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                    changed++;
                    Debug.Log($"[Migrate] '{config.name}': buyable=true → sellable=true ({path})");
                } else if (buyable.boolValue && sellable.boolValue) {
                    alreadyOk++;
                }
            }

            if (changed > 0) AssetDatabase.SaveAssets();

            string msg = $"Scanned {total} PropsConfig assets.\n" +
                         $"Set sellable=true on {changed} (were buyable, not yet sellable).\n" +
                         $"{alreadyOk} were already buyable+sellable.";
            Debug.Log($"[Migrate] Done. {msg}");
            EditorUtility.DisplayDialog("Migrate buyable → sellable", msg, "OK");
        }
    }
}
