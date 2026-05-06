#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(PropIdentity))]
public class PropIdentityEditor : Editor {
    private SerializedProperty _propIdProp;
    private SerializedProperty _roomIdProp;

    private void OnEnable() {
        _propIdProp = serializedObject.FindProperty("propId");
        _roomIdProp = serializedObject.FindProperty("roomId");
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_propIdProp);
        EditorGUILayout.PropertyField(_roomIdProp);

        int currentId = _propIdProp.intValue;
        bool hasConflict = currentId > 0 && HasConflict(target as PropIdentity);
        if (currentId <= 0) {
            EditorGUILayout.HelpBox("PropId is 0 — assign a unique ID before placing in a scene.", MessageType.Warning);
        } else if (hasConflict) {
            EditorGUILayout.HelpBox($"PropId {currentId} is already used by another PropIdentity in the loaded scenes!", MessageType.Error);
        }

        if (GUILayout.Button("Generate Unique ID")) {
            GenerateUniqueId(_propIdProp);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            // Mark the owning scene dirty so the new id is saved
            var mono = target as MonoBehaviour;
            if (mono != null) EditorSceneManager.MarkSceneDirty(mono.gameObject.scene);
            return;
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void GenerateUniqueId(SerializedProperty prop) {
        HashSet<int> used = CollectUsedIds(prop.serializedObject.targetObject as PropIdentity);
        int next = 1;
        while (used.Contains(next)) next++;
        prop.intValue = next;
    }

    private static HashSet<int> CollectUsedIds(PropIdentity exclude) {
        var used = new HashSet<int>();
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects()) {
                foreach (PropIdentity id in root.GetComponentsInChildren<PropIdentity>(true)) {
                    if (id == exclude) continue;
                    if (id.PropId > 0) used.Add(id.PropId);
                }
            }
        }
        return used;
    }

    private static bool HasConflict(PropIdentity target) {
        if (target == null || target.PropId <= 0) return false;
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects()) {
                foreach (PropIdentity id in root.GetComponentsInChildren<PropIdentity>(true)) {
                    if (id != target && id.PropId == target.PropId) return true;
                }
            }
        }
        return false;
    }
}
#endif
