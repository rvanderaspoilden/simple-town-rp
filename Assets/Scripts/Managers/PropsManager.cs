using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Sim.Building;
using Sim.Scriptables;
using Sim.Utils;
using UnityEngine;

namespace Sim {
    public class PropsManager : MonoBehaviour {
        private Dictionary<int, PropsConfig> propsConfigs;

        public static PropsManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this);
            }

            Instance = this;
        }

        private void Start() {
            this.propsConfigs = DatabaseManager.PropsDatabase.GetProps().ToDictionary(config => config.GetId(), config => config);
        }

        public void DestroyProps(Props props, bool network) {
            if (network) {
                PhotonNetwork.Destroy(props.photonView);
            } else {
                Destroy(props.gameObject);
            }
        }

        public Props InstantiateProps(PropsConfig config, Vector3 position, Quaternion rotation, bool network) {
            GameObject propsInstanciated;

            if (network) {
                // TODO: be carefull PhotonNetwork.InstantiateSceneObject needs to be masterclient to works
                propsInstanciated = PhotonNetwork.InstantiateRoomObject(CommonUtils.GetRelativePathFromResources(config.GetPrefab()), position, rotation);
            } else {
                propsInstanciated = Instantiate(config.GetPrefab().gameObject, position, rotation);
            }

            Props props = propsInstanciated.GetComponent<Props>();
            props.SetConfiguration(Instantiate(config));

            return props;
        }

        public Props InstantiateProps(PropsConfig config, bool network) {
            return this.InstantiateProps(config, Vector3.zero, config.GetPrefab().transform.rotation, network);
        }

        public Props InstantiateProps(int propsConfigId, Vector3 position, Quaternion rotation, bool network) {
            if (!this.propsConfigs.ContainsKey(propsConfigId)) {
                Debug.LogError("Props config ID : " + propsConfigId + " not found in database");
                return null;
            }

            return this.InstantiateProps(this.propsConfigs[propsConfigId], position, rotation, network);
        }

        public Props InstantiateProps(int propsConfigId, bool network) {
            if (!this.propsConfigs.ContainsKey(propsConfigId)) {
                Debug.LogError("Props config ID : " + propsConfigId + " not found in database");
                return null;
            }

            return this.InstantiateProps(this.propsConfigs[propsConfigId], Vector3.zero, this.propsConfigs[propsConfigId].GetPrefab().transform.rotation, network);
        }
    }
}