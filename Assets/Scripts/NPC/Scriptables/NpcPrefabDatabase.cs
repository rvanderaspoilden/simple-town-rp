using System.Collections.Generic;
using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// Mapping prefabId (string) → prefab Unity.
    /// Doit être placé sous Assets/Resources/Configurations/Databases/ pour être
    /// chargeable via Resources.Load (cf. DatabaseManager). Le serveur ne s'en
    /// sert PAS pour instancier (il vit dans la simulation pure), mais le client
    /// doit pouvoir résoudre prefabId → prefab à la réception d'un S2C_SpawnNpc.
    /// </summary>
    [CreateAssetMenu(menuName = "SimpleTown/NPC/NpcPrefabDatabase", fileName = "NpcPrefabDatabase")]
    public class NpcPrefabDatabase : ScriptableObject {
        [System.Serializable]
        public struct Entry {
            public string     PrefabId;
            public GameObject Prefab;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<string, GameObject> _index;

        public GameObject GetPrefab(string prefabId) {
            BuildIndex();
            return _index.TryGetValue(prefabId, out GameObject go) ? go : null;
        }

        public IReadOnlyList<Entry> Entries => entries;

        private void BuildIndex() {
            if (_index != null) return;
            _index = new Dictionary<string, GameObject>(entries.Count);
            foreach (var e in entries) {
                if (!string.IsNullOrEmpty(e.PrefabId) && e.Prefab != null)
                    _index[e.PrefabId] = e.Prefab;
            }
        }
    }
}
