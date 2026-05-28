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
        public static List<NotificationTemplateConfig> NotificationTemplateConfigs;
        public static List<SubGameConfiguration> SubGameConfigurations;
        public static List<BuildingConfig> BuildingConfigurations;

        // Lookups O(1) construits au chargement.
        private static Dictionary<int, PropsConfig> _propsById;
        private static Dictionary<int, CoverConfig> _paintsById;
        private static Dictionary<int, ItemConfig> _itemsById;

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

            NotificationTemplateConfigs = Resources.LoadAll<NotificationTemplateConfig>("Configurations/Notifications").ToList();
            Debug.Log("Notification Template Configs loaded : " + NotificationTemplateConfigs.Count);

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
