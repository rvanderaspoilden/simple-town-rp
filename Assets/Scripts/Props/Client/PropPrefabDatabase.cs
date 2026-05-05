using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject mapping prefabId strings to GameObjects.
/// One asset must live at Resources/Configurations/PropPrefabDatabase
/// so both the server (pure C# ServerPropManager) and the client
/// (ClientPropManager) can resolve prefabs through PropPrefabDatabase.Instance.
/// </summary>
[CreateAssetMenu(fileName = "PropPrefabDatabase", menuName = "The Broz/Prop Prefab Database")]
public class PropPrefabDatabase : ScriptableObject {
    private const string ResourcePath = "Configurations/PropPrefabDatabase";

    [Serializable]
    public struct Entry {
        public string     Id;
        public GameObject Prefab;
    }

    [SerializeField] private Entry[] entries;

    private Dictionary<string, GameObject> _cache;

    private static PropPrefabDatabase _instance;
    public  static PropPrefabDatabase Instance =>
        _instance != null ? _instance : (_instance = Resources.Load<PropPrefabDatabase>(ResourcePath));

    private void OnEnable() => RebuildCache();

    public GameObject GetPrefab(string id) {
        if (_cache == null) RebuildCache();
        return _cache.TryGetValue(id, out var prefab) ? prefab : null;
    }

    private void RebuildCache() {
        _cache = new Dictionary<string, GameObject>(entries?.Length ?? 0, StringComparer.Ordinal);
        if (entries == null) return;
        foreach (var e in entries) {
            if (!string.IsNullOrEmpty(e.Id) && e.Prefab != null)
                _cache[e.Id] = e.Prefab;
        }
    }
}
