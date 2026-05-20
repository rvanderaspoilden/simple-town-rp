using System.Collections.Generic;
using Sim.Enums;
using Sim.Scriptables;
using UnityEditor;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace SimEditor {
    /// <summary>
    /// One-shot maintenance command: removes the legacy SELL action (which simply
    /// deleted the prop) from every PropsConfig's actions / unbuiltActions arrays.
    /// SELL is superseded by the player-to-player sale flow (LIST_FOR_SALE / BUY,
    /// injected dynamically). The SELL Action asset itself is left untouched.
    ///
    /// Run "Tools/The BROZ/Remove SELL Action From All Props". Idempotent.
    /// </summary>
    public static class RemoveSellActionTool {
        [MenuItem("Tools/The BROZ/Remove SELL Action From All Props")]
        public static void RemoveSellActions() {
            string[] guids = AssetDatabase.FindAssets("t:PropsConfig");
            int changedConfigs = 0;
            int removedRefs = 0;

            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PropsConfig config = AssetDatabase.LoadAssetAtPath<PropsConfig>(path);
                if (config == null) continue;

                SerializedObject so = new SerializedObject(config);
                int removedHere = 0;
                removedHere += StripSell(so.FindProperty("actions"));
                removedHere += StripSell(so.FindProperty("unbuiltActions"));

                if (removedHere > 0) {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                    changedConfigs++;
                    removedRefs += removedHere;
                    Debug.Log($"[RemoveSellActionTool] Removed {removedHere} SELL ref(s) from '{config.name}' ({path})");
                }
            }

            if (changedConfigs > 0) {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[RemoveSellActionTool] Done. Scanned {guids.Length} PropsConfig assets, " +
                      $"removed {removedRefs} SELL reference(s) across {changedConfigs} config(s).");
            EditorUtility.DisplayDialog("Remove SELL Action",
                $"Scanned {guids.Length} PropsConfig assets.\nRemoved {removedRefs} SELL reference(s) across {changedConfigs} config(s).",
                "OK");
        }

        /// <summary>
        /// Rebuilds an Action[] SerializedProperty array, dropping any element whose
        /// referenced Action has Type == SELL. Returns the number removed. Rebuilding
        /// (rather than DeleteArrayElementAtIndex) avoids the object-reference array
        /// "delete leaves a null" quirk.
        /// </summary>
        private static int StripSell(SerializedProperty arrayProp) {
            if (arrayProp == null || !arrayProp.isArray) return 0;

            List<Object> kept = new List<Object>();
            int removed = 0;
            for (int i = 0; i < arrayProp.arraySize; i++) {
                Object obj = arrayProp.GetArrayElementAtIndex(i).objectReferenceValue;
                Action action = obj as Action;
                if (action != null && action.Type == ActionTypeEnum.SELL) {
                    removed++;
                    continue;
                }
                kept.Add(obj);
            }

            if (removed == 0) return 0;

            arrayProp.arraySize = kept.Count;
            for (int i = 0; i < kept.Count; i++)
                arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];

            return removed;
        }
    }
}
