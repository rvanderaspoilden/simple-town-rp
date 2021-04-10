using System.Collections.Generic;
using System.Linq;
using Sim.Building;
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
            this.propsConfigs = DatabaseManager.PropsDatabase.GetProps().ToDictionary(config => config.GetId(), config => config);
        }

        public Props InstantiateProps(PropsConfig config, int presetId, Vector3 position, Quaternion rotation) {
            Props props = Instantiate(config.GetPrefab(), position, rotation);

            props.SetConfiguration(Instantiate(config));

            if (presetId != -1) {
                props.PresetId = presetId;
            }

            return props;
        }

        public Props InstantiateProps(PropsConfig config, int presetId) {
            return this.InstantiateProps(config, presetId, Vector3.zero, config.GetPrefab().transform.rotation);
        }

        public Props InstantiateProps(int propsConfigId, int presetId, Vector3 position, Quaternion rotation) {
            if (!this.propsConfigs.ContainsKey(propsConfigId)) {
                Debug.LogError("Props config ID : " + propsConfigId + " not found in database");
                return null;
            }

            return this.InstantiateProps(this.propsConfigs[propsConfigId], presetId, position, rotation);
        }

        public Props InstantiateProps(int propsConfigId, int presetId) {
            if (!this.propsConfigs.ContainsKey(propsConfigId)) {
                Debug.LogError("Props config ID : " + propsConfigId + " not found in database");
                return null;
            }

            return this.InstantiateProps(this.propsConfigs[propsConfigId], presetId, Vector3.zero, this.propsConfigs[propsConfigId].GetPrefab().transform.rotation);
        }
    }
}