using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

namespace Sim {
    public class DatabaseManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private Material transparentMaterial;
        [SerializeField] private Material unbuiltMaterial;
        [SerializeField] private Material errorMaterial;
        
        public static PropsDatabaseConfig PropsDatabase;
        public static PaintDatabaseConfig PaintDatabase;
        public static List<MoodConfig> MoodConfigs;

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

            SetPhotonPool();
            
            DontDestroyOnLoad(this.gameObject);
        }

        private static void SetPhotonPool() {
            if (PhotonNetwork.PrefabPool is DefaultPool pool)
            {
                foreach (PropsConfig propsConfig in PropsDatabase.GetProps())
                {
                    pool.ResourceCache.Add(propsConfig.GetPrefab().name, propsConfig.GetPrefab().gameObject);
                }
            }
        }

        public static MoodConfig GetMoodConfigByEnum(MoodEnum moodEnum) {
            return MoodConfigs.Find(config => config.MoodEnum == moodEnum);
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