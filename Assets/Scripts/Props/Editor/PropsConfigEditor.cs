#if UNITY_EDITOR
using System.Collections.Generic;
using Sim.Scriptables;
using UnityEditor;
using UnityEngine;

namespace Sim.Editor {
    [CustomEditor(typeof(PropsConfig))]
    public class PropsConfigEditor : UnityEditor.Editor {
        private SerializedProperty _idProp;

        private void OnEnable() {
            _idProp = serializedObject.FindProperty("id");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            DrawDefaultInspector();

            int currentId = _idProp.intValue;
            PropsConfig self = target as PropsConfig;

            if (currentId <= 0) {
                EditorGUILayout.HelpBox("ID is 0 — assign a unique ID before using this config.", MessageType.Warning);
            } else {
                PropsConfig duplicate = FindDuplicate(self, currentId);
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

        private static int GenerateUniqueId(PropsConfig exclude) {
            HashSet<int> used = CollectUsedIds(exclude);
            int next = 1;
            while (used.Contains(next)) next++;
            return next;
        }

        private static HashSet<int> CollectUsedIds(PropsConfig exclude) {
            var used = new HashSet<int>();
            string[] guids = AssetDatabase.FindAssets("t:PropsConfig");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PropsConfig cfg = AssetDatabase.LoadAssetAtPath<PropsConfig>(path);
                if (cfg == null || cfg == exclude) continue;
                int id = cfg.GetId();
                if (id > 0) used.Add(id);
            }
            return used;
        }

        private static PropsConfig FindDuplicate(PropsConfig self, int id) {
            string[] guids = AssetDatabase.FindAssets("t:PropsConfig");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PropsConfig cfg = AssetDatabase.LoadAssetAtPath<PropsConfig>(path);
                if (cfg == null || cfg == self) continue;
                if (cfg.GetId() == id) return cfg;
            }
            return null;
        }
    }
}
#endif
