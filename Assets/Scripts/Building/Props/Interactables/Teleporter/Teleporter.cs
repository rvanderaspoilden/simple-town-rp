using System.Collections;
using Mirror;
using Sim.Building;
using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public class Teleporter : Props {
        [Header("Settings")]
        [SerializeField]
        private HallController hallPrefab;
        
        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.TELEPORT)) {
                this.CmdGoToHall();
            }
        }
        
        [Command(requiresAuthority = false)]
        public void CmdGoToHall(NetworkConnectionToClient sender = null) {
            Debug.Log($"{sender.connectionId} want to go to hall");
            HallController hall = Instantiate(hallPrefab, new Vector3(0, -50, 0), Quaternion.identity);
            
            NetworkServer.Spawn(hall.gameObject);
            
            hall.Init(1);
            
            hall.MoveToSpawn(sender.identity.gameObject);
        }
    }
}