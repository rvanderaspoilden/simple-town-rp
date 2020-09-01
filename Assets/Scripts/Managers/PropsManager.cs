using Photon.Pun;
using Sim.Building;
using Sim.Scriptables;
using Sim.Utils;
using UnityEngine;

namespace Sim {
    public class PropsManager : MonoBehaviour {
        public static PropsManager instance;

        private void Awake() {
            instance = this;
        }

        public Props InstantiateProps(PropsConfig config, Vector3 position, Quaternion rotation, bool network) {
            GameObject propsInstanciated;

            if (network) {
                propsInstanciated = PhotonNetwork.InstantiateSceneObject(CommonUtils.GetRelativePathFromResources(config.GetPrefab()), position, rotation);
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
    }   
}
