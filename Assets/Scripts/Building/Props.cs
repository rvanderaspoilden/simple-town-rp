using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Building {
    public class Props : MonoBehaviourPun {
        [Header("Props settings")]
        [SerializeField] protected PropsConfig configuration;

        protected Action[] actions;

        protected Action[] unbuiltActions;

        protected bool built;
        
        protected Renderer[] renderersToModify;

        protected Dictionary<Renderer, Material[]> defaultMaterialsByRenderer;

        protected virtual void Awake() {
            this.built = true;
            this.renderersToModify = GetComponentsInChildren<Renderer>();
            this.defaultMaterialsByRenderer = this.renderersToModify.ToList().ToDictionary(x => x, x => x.materials);
        }

        protected virtual void Start() {
            this.SetupActions();
            this.SetupUnbuiltActions();
        }

        protected virtual void SetupActions() {
            this.actions = this.configuration.GetActions();
        }

        protected virtual void SetupUnbuiltActions() {
            this.unbuiltActions = this.configuration.GetUnbuiltActions();
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
            } else {
                photonView.RPC("RPC_SetIsBuilt", RpcTarget.All, value);
            }
        }

        [PunRPC]
        public void RPC_SetIsBuilt(bool value) {
            if (this.renderersToModify == null) {
                this.renderersToModify = GetComponentsInChildren<Renderer>();
                this.defaultMaterialsByRenderer = this.renderersToModify.ToList().ToDictionary(x => x, x => x.materials);
            }

            this.built = value;

            foreach (Renderer renderer in this.renderersToModify) {
                Material[] newMaterials = new Material[renderer.materials.Length];

                for (int i = 0; i < renderer.materials.Length; i++) {
                    newMaterials[i] = this.built ? this.defaultMaterialsByRenderer[renderer][i] : DatabaseManager.Instance.GetUnbuiltMaterial();
                }

                renderer.materials = newMaterials;
            }
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
            }
        }

        public virtual void Use() {
            throw new NotImplementedException();
        }

        public virtual void Build() {
            this.SetIsBuilt(true);
            
            RoomManager.Instance.SaveRoom();
        }

        public virtual void Move() {
            throw new NotImplementedException();
        }

        public void UpdateTransform(Photon.Realtime.Player playerTarget = null) {
            if (playerTarget == null) {
                photonView.RPC("RPC_UpdateTransform", RpcTarget.Others, this.transform.position, this.transform.rotation);
            } else {
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
    }
}