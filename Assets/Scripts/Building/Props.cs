using Photon.Pun;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class Props : MonoBehaviourPun {
        [Header("Settings")]
        [SerializeField] protected PropsConfig configuration;
        
        public void UpdateTransform() {
            photonView.RPC("RPC_UpdateTransform", RpcTarget.OthersBuffered, this.transform.position, this.transform.rotation);    
        }

        public PropsConfig GetConfiguration() {
            return this.configuration;
        }
        
        [PunRPC]
        public void RPC_UpdateTransform(Vector3 pos, Quaternion rot) {
            this.transform.position = pos;
            this.transform.rotation = rot;
        }
    }
}
