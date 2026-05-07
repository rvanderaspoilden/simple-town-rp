using Mirror;
using UnityEngine;

namespace Sim.Logging {
    [DefaultExecutionOrder(-1000)]
    public class ServerLoggerInitializer : MonoBehaviour {
        [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private bool serverOnly = true;

        private void Awake() {
            if (!initializeOnAwake) return;

            if (serverOnly && !NetworkServer.active && !Application.isBatchMode) {
                return;
            }

            LoggerBootstrap.Initialize();
            GameLogger.System.Info("ServerLoggerInitializer executed on {MachineName}", System.Environment.MachineName);
            DontDestroyOnLoad(this.gameObject);
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
            LoggerBootstrap.Initialize();
            GameLogger.System.Info("Logger manually initialized");
        }
    }
}
