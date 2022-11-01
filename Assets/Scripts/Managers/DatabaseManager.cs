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

        public static PropsDatabaseConfig PropsDatabase;
        public static PaintDatabaseConfig PaintDatabase;
        public static List<MoodConfig> MoodConfigs;
        public static List<ShopCategoryConfig> ShopCategoryConfigs;
        public static List<ItemConfig> ItemConfigs;
        public static GameConfiguration GameConfiguration;
        public static List<NotificationTemplateConfig> NotificationTemplateConfigs;
        public static List<SubGameConfiguration> SubGameConfigurations;
        public static List<BuildAreaConfig> BuildAreaConfigurations;

        public static DatabaseManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }

            PropsDatabase = Resources.Load<PropsDatabaseConfig>("Configurations/Databases/Props Database");
            Debug.Log("Props database loaded");

            PaintDatabase = Resources.Load<PaintDatabaseConfig>("Configurations/Databases/Paint Database");
            Debug.Log("Paint database loaded");

            MoodConfigs = Resources.LoadAll<MoodConfig>("Configurations/Moods").ToList();
            Debug.Log("Mood Configs loaded : " + MoodConfigs.Count);
            
            NotificationTemplateConfigs = Resources.LoadAll<NotificationTemplateConfig>("Configurations/Notifications").ToList();
            Debug.Log("Notification Template Configs loaded : " + NotificationTemplateConfigs.Count);
            
            SubGameConfigurations = Resources.LoadAll<SubGameConfiguration>("Configurations/Sub Games").ToList();
            Debug.Log("Sub Game Configs loaded : " + SubGameConfigurations.Count);
            
            ShopCategoryConfigs = Resources.LoadAll<ShopCategoryConfig>("Configurations/Shop/Categories").ToList();
            Debug.Log("Shop Category Configs loaded : " + ShopCategoryConfigs.Count);
            
            ItemConfigs = Resources.LoadAll<ItemConfig>("Configurations/Items").ToList();
            Debug.Log("Item Configs loaded : " + ItemConfigs.Count);
            
            GameConfiguration = Resources.Load<GameConfiguration>("Configurations/Game Configuration");
            Debug.Log($"Game configuration loaded : 1");
            
            BuildAreaConfigurations = Resources.LoadAll<BuildAreaConfig>("Configurations/Build Areas").ToList();
            Debug.Log($"Build Area configurations loaded : {BuildAreaConfigurations.Count}");

            RegisterPrefabs();

            DontDestroyOnLoad(this.gameObject);
        }

        private static void RegisterPrefabs() {
            foreach (PropsConfig propsConfig in PropsDatabase.GetProps()) {
                NetworkManager.singleton.spawnPrefabs.Add(propsConfig.GetPrefab().gameObject);
            }
            
            foreach (ItemConfig config in ItemConfigs) {
                NetworkManager.singleton.spawnPrefabs.Add(config.Prefab.gameObject);
            }
            
            foreach (BuildAreaConfig config in BuildAreaConfigurations) {
                NetworkManager.singleton.spawnPrefabs.Add(config.Prefab.gameObject);
            }
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