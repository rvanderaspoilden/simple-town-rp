using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

namespace Sim {
    public class DatabaseManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Material transparentMaterial;

        [SerializeField]
        private Material unbuiltMaterial;

        [SerializeField]
        private Material errorMaterial;

        public static List<PropsConfig> PropsConfigs;
        public static List<CoverConfig> PaintConfigs;
        public static List<MoodConfig> MoodConfigs;
        public static List<ShopCategoryConfig> ShopCategoryConfigs;
        public static List<ItemConfig> ItemConfigs;
        public static GameConfiguration GameConfiguration;
        public static List<SubGameConfiguration> SubGameConfigurations;
        public static List<BuildingConfig> BuildingConfigurations;
        public static List<MinimapRoomMapConfig> MinimapRoomMapConfigs;
        public static List<VehicleConfig> VehicleConfigs;
        public static List<Sim.NPC.NpcConfig> NpcConfigs;

        /// <summary>NpcConfig de fallback (id == "default") pour les NPC sans config explicite.</summary>
        public static Sim.NPC.NpcConfig DefaultNpcConfig { get; private set; }

        // Lookups O(1) construits au chargement.
        private static Dictionary<int, PropsConfig> _propsById;
        private static Dictionary<int, CoverConfig> _paintsById;
        private static Dictionary<int, ItemConfig> _itemsById;
        private static Dictionary<string, MinimapRoomMapConfig> _minimapByRoomId;
        private static Dictionary<string, VehicleConfig> _vehiclesById;
        private static Dictionary<string, Sim.NPC.NpcConfig> _npcConfigsById;

        public static DatabaseManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }

            PropsConfigs = Resources.LoadAll<PropsConfig>("Configurations/Props").ToList();
            _propsById = BuildIndex(PropsConfigs, p => p.GetId());
            Debug.Log("Props configs loaded : " + PropsConfigs.Count);

            PaintConfigs = Resources.LoadAll<CoverConfig>("Configurations/Covers").ToList();
            _paintsById = BuildIndex(PaintConfigs, p => p.GetId());
            Debug.Log("Paint configs loaded : " + PaintConfigs.Count);

            MoodConfigs = Resources.LoadAll<MoodConfig>("Configurations/Moods").ToList();
            Debug.Log("Mood Configs loaded : " + MoodConfigs.Count);

            SubGameConfigurations = Resources.LoadAll<SubGameConfiguration>("Configurations/Sub Games").ToList();
            Debug.Log("Sub Game Configs loaded : " + SubGameConfigurations.Count);

            ShopCategoryConfigs = Resources.LoadAll<ShopCategoryConfig>("Configurations/Shop/Categories").ToList();
            Debug.Log("Shop Category Configs loaded : " + ShopCategoryConfigs.Count);

            ItemConfigs = Resources.LoadAll<ItemConfig>("Configurations/Items").ToList();
            _itemsById = BuildIndex(ItemConfigs, c => c.ID);
            Debug.Log("Item Configs loaded : " + ItemConfigs.Count);

            GameConfiguration = Resources.Load<GameConfiguration>("Configurations/Game Configuration");
            Debug.Log($"Game configuration loaded : 1");

            BuildingConfigurations = Resources.LoadAll<BuildingConfig>("Configurations/Buildings").ToList();
            Debug.Log($"Building configurations loaded : {BuildingConfigurations.Count}");

            MinimapRoomMapConfigs = Resources.LoadAll<MinimapRoomMapConfig>("Configurations/Minimap").ToList();
            _minimapByRoomId = new Dictionary<string, MinimapRoomMapConfig>();
            foreach (var m in MinimapRoomMapConfigs) {
                if (m == null || string.IsNullOrEmpty(m.RoomId)) continue;
                _minimapByRoomId[m.RoomId] = m;
            }
            Debug.Log($"Minimap room map configs loaded : {MinimapRoomMapConfigs.Count}");

            VehicleConfigs = Resources.LoadAll<VehicleConfig>("Configurations/Vehicles").ToList();
            _vehiclesById = new Dictionary<string, VehicleConfig>();
            foreach (var v in VehicleConfigs) {
                if (v == null) continue;
                if (!string.IsNullOrEmpty(v.id)) _vehiclesById[v.id] = v;
                // Repli par nom de modèle (anciennes lignes DB qui stockaient le modelName).
                if (!string.IsNullOrEmpty(v.modelName) && !_vehiclesById.ContainsKey(v.modelName))
                    _vehiclesById[v.modelName] = v;
            }
            Debug.Log($"Vehicle configs loaded : {VehicleConfigs.Count}");

            NpcConfigs = Resources.LoadAll<Sim.NPC.NpcConfig>("Configurations/NPCs").ToList();
            _npcConfigsById = new Dictionary<string, Sim.NPC.NpcConfig>();
            foreach (var n in NpcConfigs) {
                if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                if (_npcConfigsById.ContainsKey(n.Id)) {
                    Debug.LogWarning($"[DatabaseManager] Duplicate NpcConfig id '{n.Id}'");
                    continue;
                }
                _npcConfigsById[n.Id] = n;
                if (n.Id == "default") DefaultNpcConfig = n;
            }
            if (DefaultNpcConfig == null)
                Debug.LogWarning("[DatabaseManager] No NpcConfig with id 'default' — passersby will have no dialogue.");
            Debug.Log($"NPC configs loaded : {NpcConfigs.Count}");

            RegisterPrefabs();

            DontDestroyOnLoad(this.gameObject);
        }

        private static Dictionary<int, T> BuildIndex<T>(IEnumerable<T> list, System.Func<T, int> idSelector) where T : class {
            var dict = new Dictionary<int, T>();
            foreach (var item in list) {
                if (item == null) continue;
                int id = idSelector(item);
                if (id <= 0) continue;
                if (dict.ContainsKey(id)) {
                    Debug.LogWarning($"[DatabaseManager] Duplicate id {id} for {typeof(T).Name}");
                    continue;
                }
                dict[id] = item;
            }
            return dict;
        }

        private static void RegisterPrefabs() {
            // Props are no longer registered here - the new PropBehaviourBase system
            // uses ServerPropManager/ClientPropManager which instantiate prefabs
            // directly without Mirror's NetworkServer.Spawn()

            if (NetworkManager.singleton == null) {
                Debug.LogWarning("[DatabaseManager] RegisterPrefabs ignoré : NetworkManager.singleton est null " +
                                 "(ordre d'init). Les prefabs de bâtiments ne seront pas enregistrés.");
                return;
            }

            foreach (BuildingConfig config in BuildingConfigurations) {
                if (config == null || config.Prefab == null) continue;
                NetworkManager.singleton.spawnPrefabs.Add(config.Prefab.gameObject);
            }

            // Véhicules : tout prefab portant un VehicleController sous Resources/Prefabs/Vehicles
            // est enregistré comme spawnable Mirror (spawn serveur via VehicleSpawner).
            foreach (VehicleController vehicle in Resources.LoadAll<VehicleController>("Prefabs/Vehicles")) {
                if (vehicle == null) continue;
                if (!NetworkManager.singleton.spawnPrefabs.Contains(vehicle.gameObject))
                    NetworkManager.singleton.spawnPrefabs.Add(vehicle.gameObject);
            }
        }

        // ── Lookups ──────────────────────────────────────────────────────────────

        public static PropsConfig GetPropsById(int id)
            => _propsById != null && _propsById.TryGetValue(id, out var p) ? p : null;

        public static CoverConfig GetPaintById(int id)
            => _paintsById != null && _paintsById.TryGetValue(id, out var p) ? p : null;

        public static ItemConfig GetItemConfigById(int id)
            => _itemsById != null && _itemsById.TryGetValue(id, out var c) ? c : null;

        public static GameObject GetItemPrefab(int itemConfigId) {
            var cfg = GetItemConfigById(itemConfigId);
            return cfg != null && cfg.Prefab != null ? cfg.Prefab.gameObject : null;
        }

        public static MoodConfig GetMoodConfigByEnum(MoodEnum moodEnum) {
            return MoodConfigs.Find(config => config.MoodEnum == moodEnum);
        }

        public static MinimapRoomMapConfig GetMinimapRoomMapByRoomId(string roomId)
            => _minimapByRoomId != null && !string.IsNullOrEmpty(roomId)
               && _minimapByRoomId.TryGetValue(roomId, out var m) ? m : null;

        /// <summary>Résout une config véhicule par son id (repli sur le modelName pour les anciennes lignes).</summary>
        public static VehicleConfig GetVehicleConfigById(string id)
            => _vehiclesById != null && !string.IsNullOrEmpty(id)
               && _vehiclesById.TryGetValue(id, out var v) ? v : null;

        /// <summary>Résout une config NPC par son id. Retourne null si introuvable.</summary>
        public static Sim.NPC.NpcConfig GetNpcConfigById(string id)
            => _npcConfigsById != null && !string.IsNullOrEmpty(id)
               && _npcConfigsById.TryGetValue(id, out var n) ? n : null;

        public static ShopCategoryConfig GetShopCategoryByPropsType(PropsType propsType) {
            return ShopCategoryConfigs.Find(config => config.PropsType == propsType);
        }

        public static List<ConsumableConfig> GetConsumableItems() {
            return (List<ConsumableConfig>)ItemConfigs.Where(x => x.Type == ItemType.CONSUMABLE);
        }

        public Material GetTransparentMaterial() {
            return this.transparentMaterial;
        }

        public Material GetUnbuiltMaterial() {
            return this.unbuiltMaterial;
        }

        public Material GetErrorMaterial() {
            return this.errorMaterial;
        }
    }
}
