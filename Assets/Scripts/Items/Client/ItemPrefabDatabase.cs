using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that holds the list of ItemConfig assets and resolves itemConfigId → prefab.
/// Place at Resources/Configurations/Items/ItemPrefabDatabase.
/// No prefab references ever transit the network — resolution is always local.
/// </summary>
[CreateAssetMenu(fileName = "ItemPrefabDatabase", menuName = "SimpleTown/Item Prefab Database")]
public class ItemPrefabDatabase : ScriptableObject
{
    [SerializeField] private List<ItemConfig> items = new List<ItemConfig>();

    private Dictionary<int, GameObject> _lookup;

    private void BuildLookup()
    {
        _lookup = new Dictionary<int, GameObject>(items.Count);
        foreach (var config in items)
        {
            if (config == null) continue;
            if (config.Prefab == null)
            {
                Debug.LogWarning($"[ItemPrefabDatabase] Null prefab for itemConfigId={config.ID}");
                continue;
            }
            _lookup[config.ID] = config.Prefab.gameObject;
        }
    }

    public GameObject GetPrefab(int itemConfigId)
    {
        if (_lookup == null) BuildLookup();
        _lookup.TryGetValue(itemConfigId, out var prefab);
        return prefab;
    }
}
