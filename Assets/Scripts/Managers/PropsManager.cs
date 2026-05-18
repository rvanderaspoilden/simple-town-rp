using System.Collections.Generic;
using System.Linq;
using Sim.Scriptables;
using UnityEngine;

namespace Sim {
    public class PropsManager : MonoBehaviour {
        private Dictionary<int, PropsConfig> propsConfigs;

        public static PropsManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this);
            } else {
                Instance = this;
            }
        }

        private void Start() {
            this.propsConfigs = DatabaseManager.PropsConfigs.ToDictionary(config => config.GetId(), config => config);
        }

        public PropBehaviourBase InstantiateProps(PropsConfig config, int presetId, Vector3 position, Quaternion rotation) {
            PropBehaviourBase behaviour = Instantiate(config.GetPrefab(), position, rotation);

            // Clone the configuration and assign it
            PropsConfig configInstance = Instantiate(config);
            behaviour.SetConfiguration(configInstance);

            if (presetId != -1) {
                behaviour.SetDefaultPresetId(presetId);
                // Apply the preset visual on the build preview so the player sees the correct variant
                behaviour.ApplyPresetVisual(presetId);
            }

            return behaviour;
        }

        public PropBehaviourBase InstantiateProps(PropsConfig config, int presetId) {
            return this.InstantiateProps(config, presetId, Vector3.zero, config.GetPrefab().transform.rotation);
        }

        public PropBehaviourBase InstantiateProps(int propsConfigId, int presetId, Vector3 position, Quaternion rotation) {
            if (!this.propsConfigs.ContainsKey(propsConfigId)) {
                Debug.LogError("Props config ID : " + propsConfigId + " not found in database");
                return null;
            }

            return this.InstantiateProps(this.propsConfigs[propsConfigId], presetId, position, rotation);
        }

        public PropBehaviourBase InstantiateProps(int propsConfigId, int presetId) {
            if (!this.propsConfigs.ContainsKey(propsConfigId)) {
                Debug.LogError("Props config ID : " + propsConfigId + " not found in database");
                return null;
            }

            return this.InstantiateProps(this.propsConfigs[propsConfigId], presetId, Vector3.zero, this.propsConfigs[propsConfigId].GetPrefab().transform.rotation);
        }
    }
}