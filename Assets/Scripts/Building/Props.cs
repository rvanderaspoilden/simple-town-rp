using Photon.Pun;
using UnityEngine;

namespace Sim.Building {
    public class Props : MonoBehaviourPun
    {
        public void UpdateTransform() {
            photonView.RPC("RPC_UpdateTransform", RpcTarget.OthersBuffered, this.transform.position, this.transform.rotation);    
        }
        
        [PunRPC]
        public void RPC_UpdateTransform(Vector3 pos, Quaternion rot) {
            this.transform.position = pos;
            this.transform.rotation = rot;
        }
    }
}
