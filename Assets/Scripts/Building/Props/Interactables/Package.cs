using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Interactables {
    public class Package : Props {
        [Header("Package debug")]
        [SerializeField] private PropsConfig propsInside;

        public delegate void OnOpen(Package packageOpened);

        public static event OnOpen OnOpened;
        
        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.OPEN)) {
                OnOpened?.Invoke(this);
            }
        }

        /*public override void Synchronize(Photon.Realtime.Player playerTarget) {
            base.Synchronize(playerTarget);
            
            this.SetPropsInside(this.propsInside.GetId(), playerTarget);
        }*/

        public PropsConfig GetPropsConfigInside() {
            return this.propsInside;
        }

        /*public void SetPropsInside(int id, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetPropsInside", rpcTarget, id);
        }
        
        public void SetPropsInside(int id, Photon.Realtime.Player playerTarget) {
            photonView.RPC("RPC_SetPropsInside", playerTarget, id);
        }*/

        public void RPC_SetPropsInside(int propsId) {
            this.propsInside = DatabaseManager.PropsDatabase.GetPropsById(propsId);
        }
    }
}