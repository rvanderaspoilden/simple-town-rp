using System;
using System.Linq;
using Mirror;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Building {
    [RequireComponent(typeof(PropsRenderer))]
    public class Props : NetworkBehaviour {
        [Header("Props settings")]
        [SerializeField]
        protected PropsConfig configuration;

        private Action[] actions;

        private Action[] unbuiltActions;

        [SyncVar(hook = nameof(SetIsBuilt))]
        private bool built;

        protected PropsRenderer propsRenderer;

        private Action currentAction;

        [SyncVar(hook = nameof(SetPresetId))]
        private int presetId = -1;

        public delegate void PropsAction(Props props);

        public static event PropsAction OnMoveRequest;

        protected virtual void Awake() {
            this.built = true;
            this.propsRenderer = GetComponent<PropsRenderer>();
        }

        protected virtual void Start() {
            this.ConfigureActions();
        }

        protected virtual void OnDestroy() {
            this.UnSubscribeActions(this.actions);
            this.UnSubscribeActions(this.unbuiltActions);
        }

        public virtual void StopInteraction() {
            throw new NotImplementedException();
        }

        /**
         * Setup all action when a props is built
         */
        private void SetupActions() {
            this.actions = this.configuration.GetActions().Where(x => x).Select(Instantiate).ToArray();
            SubscribeActions(this.actions);
        }

        private void SubscribeActions(Action[] actionList) {
            foreach (var action in actionList) {
                action.OnExecute += DoAction;
            }
        }

        private void UnSubscribeActions(Action[] actionList) {
            foreach (var action in actionList) {
                action.OnExecute -= DoAction;
            }
        }

        /**
         * Setup all actions when a props is not built
         */
        private void SetupUnbuiltActions() {
            this.unbuiltActions = this.configuration.GetUnbuiltActions().Where(x => x).Select(Instantiate).ToArray();
            SubscribeActions(this.unbuiltActions);
        }

        public void ConfigureActions() {
            this.SetupActions();
            this.SetupUnbuiltActions();
        }

        public bool IsInteractable() {
            return this.IsBuilt() ? this.actions.Length > 0 : this.unbuiltActions.Length > 0;
        }

        public virtual Action[] GetActions(bool withPriority = false) {
            Action[] actionsToReturn = this.IsBuilt() ? this.actions : this.unbuiltActions;

            bool hasPermission = ApartmentManager.Instance && ApartmentManager.Instance.IsTenant(RoomManager.LocalPlayer.CharacterData);

            actionsToReturn = actionsToReturn.Where(x => (x.NeedPermission && hasPermission) || !x.NeedPermission).ToArray();

            if (withPriority) {
                actionsToReturn = actionsToReturn.SkipWhile(x => x.Type.Equals(ActionTypeEnum.SELL) || x.Type.Equals(ActionTypeEnum.MOVE)).ToArray();
            }

            return actionsToReturn;
        }

        public bool IsBuilt() {
            return this.built;
        }

        public int PresetId {
            get => presetId;
            set => presetId = value;
        }

        public void SetPresetId(int oldId, int newId) {
            this.presetId = newId;
            this.UpdatePresetRender();
        }

        private void UpdatePresetRender() {
            if (this.configuration.Presets == null || this.configuration.Presets.Length == 0) {
                return;
            }

            PropsPreset preset = this.configuration.Presets.First(x => x.ID == this.PresetId);

            if (preset != null) {
                this.propsRenderer.SetPreset(preset);
            } else {
                Debug.LogError($"Props configuration of {configuration.name} doesn't have preset with ID {this.PresetId}");
            }
        }

        public void SetIsBuilt(bool oldValue, bool newValue) {
            this.built = newValue;
            this.propsRenderer.UpdateGraphics();
        }
        
        private void DoAction(Action action) {
            Debug.Log("do action : " + action.Label);

            switch (action.Type) {
                case ActionTypeEnum.MOVE:
                    this.Move();
                    break;

                case ActionTypeEnum.BUILD:
                    this.Build();
                    break;

                case ActionTypeEnum.SELL:
                    this.Sell();
                    break;

                case ActionTypeEnum.LOOK:
                    this.Look();
                    break;

                default:
                    this.Execute(action);
                    break;
            }
        }

        protected virtual void Execute(Action action) {
            throw new NotImplementedException();
        }

        private void Build() {
            //this.SetIsBuilt(true); TODO: call server

            RoomManager.Instance.SaveRoom();
        }

        private void Look() {
            RoomManager.LocalPlayer.Look(this.transform);
        }

        private void Move() {
            OnMoveRequest?.Invoke(this);
        }

        private void Sell() {
            // photonView.RPC("RPC_SellProps", PhotonNetwork.MasterClient);
        }

        public void RPC_SellProps() {
            //PropsManager.Instance.DestroyProps(this, true);
            RoomManager.Instance.SaveRoom();
        }

        public PropsConfig GetConfiguration() {
            return this.configuration;
        }

        public void SetConfiguration(PropsConfig config) {
            this.configuration = config;
        }
        
        /*public virtual void Synchronize(Player playerTarget) {
            this.SetPresetId(this.presetId, true, playerTarget);
            this.SetIsBuilt(this.built, playerTarget);
        }*/

        public bool IsWallProps() {
            return this.configuration.GetSurfaceToPose() == BuildSurfaceEnum.WALL;
        }

        public bool IsGroundProps() {
            return this.configuration.GetSurfaceToPose() == BuildSurfaceEnum.GROUND;
        }
    }
}