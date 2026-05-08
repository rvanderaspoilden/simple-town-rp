using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that maps itemConfigId → item prefab (with ItemBehaviour component).
/// Place at Resources/Configurations/Items/ItemPrefabDatabase.
/// No prefab references ever transit the network — resolution is always local.
/// </summary>
[CreateAssetMenu(fileName = "ItemPrefabDatabase", menuName = "SimpleTown/Item Prefab Database")]
public class ItemPrefabDatabase : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public int        itemConfigId;
        public GameObject prefab;        // must have ItemBehaviour + ItemIdentity
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    private Dictionary<int, GameObject> _lookup;

    private void BuildLookup()
    {
        _lookup = new Dictionary<int, GameObject>(entries.Count);
        foreach (var e in entries)
        {
            if (e.prefab == null)
            {
                Debug.LogWarning($"[ItemPrefabDatabase] Null prefab for itemConfigId={e.itemConfigId}");
                continue;
            }
            _lookup[e.itemConfigId] = e.prefab;
        }
    }

    public GameObject GetPrefab(int itemConfigId)
    {
        if (_lookup == null) BuildLookup();
        _lookup.TryGetValue(itemConfigId, out var prefab);
        return prefab;
    }
}
