using Mirror;
using UnityEngine;

namespace Sim.Logging {
    [DefaultExecutionOrder(-1000)]
    public class ConfigurableLoggerInitializer : MonoBehaviour {
        [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private bool serverOnly = true;
        
        [Header("Logger Settings")]
        [SerializeField] private GameLoggerSettings loggerSettings = new GameLoggerSettings();

        private void Awake() {
            if (!initializeOnAwake) return;

            if (serverOnly && !NetworkServer.active && !Application.isBatchMode) {
                return;
            }

            LoggerBootstrap.Initialize(loggerSettings);
            GameLogger.System.Info("ConfigurableLoggerInitializer executed on {MachineName} with settings {AppName}", 
                System.Environment.MachineName, loggerSettings.applicationName);
        }

        private void OnApplicationQuit() {
            GameLogger.System.Info("Application shutting down");
            LoggerBootstrap.Shutdown();
        }

        private void OnDestroy() {
            if (Application.isPlaying) {
                LoggerBootstrap.Shutdown();
            }
        }

        [ContextMenu("Initialize Logger Now")]
        public void InitializeNow() {
            LoggerBootstrap.Initialize(loggerSettings);
            GameLogger.System.Info("Logger manually initialized with custom settings");
        }
    }
}
