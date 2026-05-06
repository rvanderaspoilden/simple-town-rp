#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tool to bulk-generate PropIdentity IDs for the active scene.
/// Accessible via Tools > Props > Generate Scene Prop IDs
/// </summary>
public static class PropIdentitySceneTool {
    [MenuItem("Tools/Props/Generate Scene Prop IDs")]
    public static void GenerateScenePropIds() {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.isLoaded) {
            EditorUtility.DisplayDialog("Error", "No active scene loaded.", "OK");
            return;
        }

        // Collect all PropIdentity components in the active scene
        List<PropIdentity> allProps = new List<PropIdentity>();
        foreach (GameObject root in activeScene.GetRootGameObjects()) {
            allProps.AddRange(root.GetComponentsInChildren<PropIdentity>(true));
        }

        if (allProps.Count == 0) {
            EditorUtility.DisplayDialog("No Props Found", $"No PropIdentity components found in scene '{activeScene.name}'.", "OK");
            return;
        }

        // Check for duplicates and collect used IDs
        HashSet<int> usedIds = new HashSet<int>();
        List<PropIdentity> duplicates = new List<PropIdentity>();
        List<PropIdentity> missingIds = new List<PropIdentity>();

        foreach (PropIdentity prop in allProps) {
            int id = prop.PropId;
            if (id <= 0) {
                missingIds.Add(prop);
            } else if (usedIds.Contains(id)) {
                duplicates.Add(prop);
            } else {
                usedIds.Add(id);
            }
        }

        // Also check against already used IDs from other scenes
        HashSet<int> allUsedIds = CollectAllUsedIds();

        int generatedCount = 0;
        int fixedCount = 0;

        // Generate IDs for props without IDs
        foreach (PropIdentity prop in missingIds) {
            int newId = FindNextAvailableId(allUsedIds);
            Undo.RecordObject(prop, "Generate Prop ID");
            prop.Assign(newId, prop.RoomId);
            EditorUtility.SetDirty(prop);
            allUsedIds.Add(newId);
            generatedCount++;
        }

        // Fix duplicates by generating new IDs
        foreach (PropIdentity prop in duplicates) {
            int newId = FindNextAvailableId(allUsedIds);
            Undo.RecordObject(prop, "Fix Duplicate Prop ID");
            prop.Assign(newId, prop.RoomId);
            EditorUtility.SetDirty(prop);
            allUsedIds.Add(newId);
            fixedCount++;
        }

        // Mark scene dirty if changes were made
        if (generatedCount > 0 || fixedCount > 0) {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        // Summary dialog
        string message = $"Processed {allProps.Count} PropIdentity components in scene '{activeScene.name}':\n\n";
        message += $"• {generatedCount} new IDs generated\n";
        message += $"• {fixedCount} duplicates fixed\n";
        message += $"• {allProps.Count - generatedCount - fixedCount} already valid\n";

        EditorUtility.DisplayDialog("Prop IDs Generated", message, "OK");

        // Refresh the Inspector if any objects were selected
        if (Selection.activeGameObject != null) {
            EditorUtility.SetDirty(Selection.activeGameObject);
        }
    }

    [MenuItem("Tools/Props/Validate Scene Prop IDs")]
    public static void ValidateScenePropIds() {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.isLoaded) {
            EditorUtility.DisplayDialog("Error", "No active scene loaded.", "OK");
            return;
        }

        // Collect all PropIdentity components in the active scene
        List<PropIdentity> allProps = new List<PropIdentity>();
        foreach (GameObject root in activeScene.GetRootGameObjects()) {
            allProps.AddRange(root.GetComponentsInChildren<PropIdentity>(true));
        }

        if (allProps.Count == 0) {
            EditorUtility.DisplayDialog("No Props Found", $"No PropIdentity components found in scene '{activeScene.name}'.", "OK");
            return;
        }

        // Check for issues
        HashSet<int> usedIds = new HashSet<int>();
        List<PropIdentity> duplicates = new List<PropIdentity>();
        List<PropIdentity> missingIds = new List<PropIdentity>();

        foreach (PropIdentity prop in allProps) {
            int id = prop.PropId;
            if (id <= 0) {
                missingIds.Add(prop);
            } else if (usedIds.Contains(id)) {
                duplicates.Add(prop);
            } else {
                usedIds.Add(id);
            }
        }

        // Build report
        string message = $"Validated {allProps.Count} PropIdentity components in scene '{activeScene.name}':\n\n";

        if (missingIds.Count == 0 && duplicates.Count == 0) {
            message += "All PropIdentity IDs are valid! ✓";
            EditorUtility.DisplayDialog("Validation Passed", message, "OK");
        } else {
            if (missingIds.Count > 0) {
                message += $"⚠ {missingIds.Count} props missing IDs:\n";
                foreach (var prop in missingIds.Take(10)) {
                    message += $"  - {prop.gameObject.name}\n";
                }
                if (missingIds.Count > 10) message += $"  ... and {missingIds.Count - 10} more\n";
                message += "\n";
            }

            if (duplicates.Count > 0) {
                message += $"⚠ {duplicates.Count} props with duplicate IDs:\n";
                foreach (var prop in duplicates.Take(10)) {
                    message += $"  - {prop.gameObject.name} (ID: {prop.PropId})\n";
                }
                if (duplicates.Count > 10) message += $"  ... and {duplicates.Count - 10} more\n";
            }

            message += "\nUse 'Tools > Props > Generate Scene Prop IDs' to fix these issues.";

            EditorUtility.DisplayDialog("Validation Failed", message, "OK");

            // Select the problematic props in the hierarchy
            List<Object> objectsToSelect = new List<Object>();
            objectsToSelect.AddRange(missingIds.Select(p => p.gameObject as Object));
            objectsToSelect.AddRange(duplicates.Select(p => p.gameObject as Object));
            if (objectsToSelect.Count > 0) {
                Selection.objects = objectsToSelect.ToArray();
            }
        }
    }

    private static HashSet<int> CollectAllUsedIds() {
        var used = new HashSet<int>();
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects()) {
                foreach (PropIdentity id in root.GetComponentsInChildren<PropIdentity>(true)) {
                    if (id.PropId > 0) used.Add(id.PropId);
                }
            }
        }
        return used;
    }

    private static int FindNextAvailableId(HashSet<int> usedIds) {
        int next = 1;
        while (usedIds.Contains(next)) next++;
        return next;
    }
}
#endif
