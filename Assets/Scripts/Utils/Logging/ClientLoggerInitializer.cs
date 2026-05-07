using UnityEngine;

namespace Sim.Logging {
    public class ClientLoggerInitializer : MonoBehaviour {
        [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private ClientLogger.LogLevel minimumLevel = ClientLogger.LogLevel.Information;

        private void Awake() {
            if (!initializeOnAwake) return;
            
            ClientLogger.Initialize(minimumLevel);
            ClientLogger.System("ClientLogger initialized on {DeviceType} | Level: {Level}", 
                SystemInfo.deviceType, minimumLevel);
        }

        private void OnApplicationQuit() {
            ClientLogger.System("Client application shutting down");
            ClientLogger.Shutdown();
        }

        private void OnDestroy() {
            if (Application.isPlaying) {
                ClientLogger.Shutdown();
            }
        }

        [ContextMenu("Initialize Logger Now")]
        public void InitializeNow() {
            ClientLogger.Initialize(minimumLevel);
            ClientLogger.System("ClientLogger manually initialized");
        }

        [ContextMenu("Test All Categories")]
        public void TestAllCategories() {
            ClientLogger.Network("Test Network message {Value}", 123);
            ClientLogger.Props("Test Props message {Value}", 456);
            ClientLogger.Rooms("Test Rooms message {Value}", 789);
            ClientLogger.Player("Test Player message {Value}", "Player1");
            ClientLogger.UI("Test UI message {Value}", "Click");
            ClientLogger.Audio("Test Audio message {Value}", "BGM");
            ClientLogger.Input("Test Input message {Value}", "Jump");
            ClientLogger.Warning("Test Warning message");
        }
    }
}
