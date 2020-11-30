using System;
using System.Linq;
using Photon.Pun;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Building {
    [RequireComponent(typeof(PropsRenderer))]
    public class Props : MonoBehaviourPun {
        [Header("Props settings")] 
        [SerializeField] protected PropsConfig configuration;

        protected Action[] actions;

        protected Action[] unbuiltActions;

        protected bool built;

        protected PropsRenderer propsRenderer;

        public delegate void PropsAction(Props props);
        
        public static event PropsAction OnMoveRequest;
        
        protected virtual void Awake() {
            this.built = true;
            this.propsRenderer = GetComponent<PropsRenderer>();
        }

        protected virtual void Start() {
            this.RefreshAllActions();
        }

        /**
         * Setup all action when a props is built
         */
        protected virtual void SetupActions() {
            this.actions = this.configuration.GetActions();
        }

        /**
         * Setup all actions when a props is not built
         */
        protected virtual void SetupUnbuiltActions() {
            this.unbuiltActions = this.configuration.GetUnbuiltActions();

            bool isOwner = AppartmentManager.instance && AppartmentManager.instance.IsOwner(NetworkManager.Instance.Personnage);
            this.unbuiltActions.ToList().ForEach(action => action.SetIsLocked(!isOwner));
        }

        public void RefreshAllActions() {
            this.SetupActions();
            this.SetupUnbuiltActions();
        }

        public virtual Action[] GetActions() {
            if (this.IsBuilt()) {
                return this.actions;
            }

            return this.unbuiltActions;
        }

        public bool IsBuilt() {
            return this.built;
        }

        public void SetIsBuilt(bool value, Photon.Realtime.Player playerTarget = null) {
            if (playerTarget != null) {
                photonView.RPC("RPC_SetIsBuilt", playerTarget, value);
            }
            else {
                photonView.RPC("RPC_SetIsBuilt", RpcTarget.All, value);
            }
        }

        [PunRPC]
        public void RPC_SetIsBuilt(bool value) {
            this.built = value;
            this.propsRenderer.UpdateGraphics();
        }

        public void DoAction(Action action) {
            Debug.Log("do action : " + action.GetActionLabel());

            switch (action.GetActionType()) {
                case ActionTypeEnum.USE:
                    this.Use();
                    break;
                case ActionTypeEnum.MOVE:
                    this.Move();
                    break;

                case ActionTypeEnum.BUILD:
                    this.Build();
                    break;
                
                case ActionTypeEnum.DELETE:
                    this.Delete();
                    break;
            }
        }

        protected virtual void Use() {
            throw new NotImplementedException();
        }

        protected virtual void Build() {
            this.SetIsBuilt(true);

            RoomManager.Instance.SaveRoom();
        }

        protected virtual void Move() {
            OnMoveRequest?.Invoke(this);
        }

        protected virtual void Delete() {
            photonView.RPC("RPC_DestroyProps", PhotonNetwork.MasterClient);
        }
        
        [PunRPC]
        public void RPC_DestroyProps() {
            PropsManager.Instance.DestroyProps(this, true);
            RoomManager.Instance.SaveRoom();
        }

        public void UpdateTransform(Photon.Realtime.Player playerTarget = null) {
            if (playerTarget == null) {
                photonView.RPC("RPC_UpdateTransform", RpcTarget.Others, this.transform.position, this.transform.rotation);
            }
            else {
                photonView.RPC("RPC_UpdateTransform", playerTarget, this.transform.position, this.transform.rotation);
            }
        }

        public PropsConfig GetConfiguration() {
            return this.configuration;
        }

        public void SetConfiguration(PropsConfig config) {
            this.configuration = config;
        }

        [PunRPC]
        public void RPC_UpdateTransform(Vector3 pos, Quaternion rot) {
            this.transform.position = pos;
            this.transform.rotation = rot;
        }

        public virtual void Synchronize(Photon.Realtime.Player playerTarget) {
            this.SetIsBuilt(this.built, playerTarget);
            this.UpdateTransform(playerTarget);
        }

        public bool IsWallProps() {
            return this.configuration.GetSurfaceToPose() == BuildSurfaceEnum.WALL;
        }
        
        public bool IsGroundProps() {
            return this.configuration.GetSurfaceToPose() == BuildSurfaceEnum.GROUND;
        }
    }
}