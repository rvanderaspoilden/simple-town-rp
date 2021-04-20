using Mirror;
using Sim.Building;
using Sim.Enums;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sim.Interactables {
    public class Teleporter : Props {
        [Header("Settings")]
        [SerializeField]
        private Transform spawnTransform;

        [SerializeField]
        private int floorToGo;
        
        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.TELEPORT)) {
                this.CmdUse();
            }
        }

        public Transform SpawnTransform => spawnTransform;

        [Command(requiresAuthority = false)]
        public void CmdUse(NetworkConnectionToClient sender = null) {
            Debug.Log($"{sender.connectionId} want to go to hall number {this.floorToGo}");
        }
    }
}