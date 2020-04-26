using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Sim.Building;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Interactables {
    public class Package : Props {
        [Header("Package Settings")]
        [SerializeField] private Action useAction;

        [Header("Package debug")]
        [SerializeField] private PropsConfig propsInside;

        public delegate void OnOpen(Package packageOpened);

        public static event OnOpen OnOpened;

        protected override void SetupActions() {
            useAction.SetIsLocked(!PhotonNetwork.IsMasterClient); // todo replace it by appartment owner
            this.actions = new Action[1] {useAction};
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