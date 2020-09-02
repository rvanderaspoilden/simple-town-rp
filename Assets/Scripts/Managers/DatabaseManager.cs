using System.Collections.Generic;
using Sim.Scriptables;
using UnityEditor;
using UnityEngine;

namespace Sim {
    public class DatabaseManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private Material transparentMaterial;
        [SerializeField] private Material unbuiltMaterial;
        [SerializeField] private Material errorMaterial;
        
        public static PropsDatabaseConfig PropsDatabase;
        public static PaintDatabaseConfig PaintDatabase;

        public static DatabaseManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
            
            PropsDatabase = Resources.Load<PropsDatabaseConfig>("Configurations/Databases/Props Database");
            Debug.Log("Props database loaded");
            
            PaintDatabase = Resources.Load<PaintDatabaseConfig>("Configurations/Databases/Paint Database");
            Debug.Log("Paint database loaded");
            
            DontDestroyOnLoad(this.gameObject);
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