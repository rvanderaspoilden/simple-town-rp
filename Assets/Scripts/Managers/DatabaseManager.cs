using Sim.Scriptables;
using UnityEngine;

namespace Sim {
    public class DatabaseManager : MonoBehaviour {
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
    }
   
}