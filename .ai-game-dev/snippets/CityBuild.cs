using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Shared helpers for the PolygonShops city rework build snippets.
public static class CityBuild {
    public const string PrefabRoot = "Assets/Synty/PolygonShops/Prefabs/";

    public static GameObject FindInScene(string name) {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("City");
        foreach (var root in scene.GetRootGameObjects()) {
            if (root.name == name) return root;
            var t = FindChildRecursive(root.transform, name);
            if (t != null) return t.gameObject;
        }
        return null;
    }

    static Transform FindChildRecursive(Transform parent, string name) {
        foreach (Transform c in parent) {
            if (c.name == name) return c;
            var r = FindChildRecursive(c, name);
            if (r != null) return r;
        }
        return null;
    }

    public static Transform Geometry() {
        return FindInScene("City Map").transform.Find("Geometry");
    }

    // Find-or-create a named group under the given parent.
    public static Transform Group(Transform parent, string name) {
        var t = parent.Find(name);
        if (t == null) {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "group");
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        return t;
    }

    // Instantiate a prefab (keeping the prefab link) at a world position.
    public static GameObject Place(string relPath, Transform parent, Vector3 pos, float rotY = 0f, string layer = null, float scale = 1f) {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + relPath);
        if (prefab == null) { Debug.LogError("Prefab not found: " + relPath); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
        if (scale != 1f) go.transform.localScale = Vector3.one * scale;
        if (layer != null) SetLayerRecursive(go, layer);
        Undo.RegisterCreatedObjectUndo(go, "place");
        return go;
    }

    public static void SetLayerRecursive(GameObject go, string layerName) {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0) { Debug.LogError("Unknown layer " + layerName); return; }
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    public static int DeleteChildrenByName(Transform parent, params string[] names) {
        int n = 0;
        var set = new HashSet<string>(names);
        var toKill = new List<GameObject>();
        foreach (Transform c in parent) if (set.Contains(c.name)) toKill.Add(c.gameObject);
        foreach (var go in toKill) { Undo.DestroyObjectImmediate(go); n++; }
        return n;
    }

    public static void MarkDirty() {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("City");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
