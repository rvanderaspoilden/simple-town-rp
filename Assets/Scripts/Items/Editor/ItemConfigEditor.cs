#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sim.Editor {
    [CustomEditor(typeof(ItemConfig))]
    public class ItemConfigEditor : UnityEditor.Editor {
        private SerializedProperty _idProp;

        private void OnEnable() {
            _idProp = serializedObject.FindProperty("id");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            DrawDefaultInspector();

            int currentId = _idProp.intValue;
            ItemConfig self = target as ItemConfig;

            if (currentId <= 0) {
                EditorGUILayout.HelpBox("ID is 0 — assign a unique ID before using this config.", MessageType.Warning);
            } else {
                ItemConfig duplicate = FindDuplicate(self, currentId);
                if (duplicate != null) {
                    EditorGUILayout.HelpBox(
                        $"ID {currentId} is already used by \"{duplicate.name}\" ({AssetDatabase.GetAssetPath(duplicate)})!",
                        MessageType.Error
                    );
                }
            }

            if (GUILayout.Button("Generate Unique ID")) {
                int next = GenerateUniqueId(self);
                _idProp.intValue = next;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static int GenerateUniqueId(ItemConfig exclude) {
            HashSet<int> used = CollectUsedIds(exclude);
            int next = 1;
            while (used.Contains(next)) next++;
            return next;
        }

        private static HashSet<int> CollectUsedIds(ItemConfig exclude) {
            var used = new HashSet<int>();
            string[] guids = AssetDatabase.FindAssets("t:ItemConfig");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ItemConfig cfg = AssetDatabase.LoadAssetAtPath<ItemConfig>(path);
                if (cfg == null || cfg == exclude) continue;
                if (cfg.ID > 0) used.Add(cfg.ID);
            }
            return used;
        }

        private static ItemConfig FindDuplicate(ItemConfig self, int id) {
            string[] guids = AssetDatabase.FindAssets("t:ItemConfig");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ItemConfig cfg = AssetDatabase.LoadAssetAtPath<ItemConfig>(path);
                if (cfg == null || cfg == self) continue;
                if (cfg.ID == id) return cfg;
            }
            return null;
        }
    }
}
#endif
