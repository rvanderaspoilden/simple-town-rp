using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Sim.Building;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Interactables {
    public class Package : Props {
        [Header("Package debug")]
        [SerializeField] private PropsConfig propsInside;

        public delegate void OnOpen(Package packageOpened);

        public static event OnOpen OnOpened;

        protected override void SetupActions() {
            base.SetupActions();
            
            // todo replace it by appartment owner
            this.actions.ToList().ForEach(action => action.SetIsLocked(!PhotonNetwork.IsMasterClient));
        }

        public override void Use() {
            OnOpened?.Invoke(this);
        }

        public PropsConfig GetPropsInside() {
            return this.propsInside;
        }

        public void SetPropsInside(int id) {
            photonView.RPC("RPC_SetPropsInside", RpcTarget.AllBuffered, id);
        }

        [PunRPC]
        public void RPC_SetPropsInside(int propsId) {
            this.propsInside = DatabaseManager.PropsDatabase.GetPropsById(propsId);
        }
    }
}