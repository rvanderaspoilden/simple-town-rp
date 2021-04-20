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

        [SyncVar]
        [SerializeField]
        private uint parentId;

        protected PropsRenderer propsRenderer;

        private Action currentAction;

        [SyncVar(hook = nameof(SetPresetId))]
        private int presetId = -1;

        private ApartmentController apartmentController;

        public delegate void PropsAction(Props props);

        public static event PropsAction OnMoveRequest;

        protected virtual void Awake() {
            this.built = true;
            this.propsRenderer = GetComponent<PropsRenderer>();
        }

        protected virtual void Start() {
            this.ConfigureActions();
        }

        public override void OnStartClient() {
            if (parentId == 0) return;

            Vector3 position = this.transform.position;
            this.apartmentController = NetworkIdentity.spawned.ContainsKey(this.parentId)
                ? NetworkIdentity.spawned[this.parentId].GetComponent<ApartmentController>()
                : null;

            if (!isClientOnly) return;

            if (this.apartmentController) {
                this.transform.SetParent(this.apartmentController.PropsContainer);
                this.transform.localPosition = position;
            } else {
                Debug.LogError($"Parent identity not found for props {this.name}");
            }
        }

        protected virtual void OnDestroy() {
            this.UnSubscribeActions(this.actions);
            this.UnSubscribeActions(this.unbuiltActions);
        }

        public virtual void StopInteraction() {
            throw new NotImplementedException();
        }

        public void InitBuilt(bool isBuilt) {
            this.built = isBuilt;
        }

        public uint ParentId {
            get => parentId;
            set => parentId = value;
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

            bool hasPermission = this.apartmentController && this.apartmentController.IsTenant(PlayerController.Local.CharacterData);

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
            set {
                presetId = value;
                UpdatePresetRender();
            }
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

            if (this.propsRenderer == null) {
                this.propsRenderer = GetComponent<PropsRenderer>();
            }

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

        [Client]
        private void Build() {
            this.CmdBuild();
        }

        [Command(requiresAuthority = false)]
        public void CmdBuild(NetworkConnectionToClient sender = null) {
            // TODO(security): Check if sender is allowed to build props
            this.built = true;

            Debug.Log($"Server: player {sender.identity.netId} built {this.name}");

            StartCoroutine(GetComponentInParent<ApartmentController>().Save());
        }

        [Client]
        private void Look() {
            PlayerController.Local.Look(this.transform);
        }

        [Client]
        private void Move() {
            OnMoveRequest?.Invoke(this);
        }

        [Client]
        private void Sell() {
            PlayerController.Local.Sell(this);
        }

        public PropsConfig GetConfiguration() {
            return this.configuration;
        }

        public void SetConfiguration(PropsConfig config) {
            this.configuration = config;
        }

        public bool IsWallProps() {
            return this.configuration.GetSurfaceToPose() == BuildSurfaceEnum.WALL;
        }

        public bool IsGroundProps() {
            return this.configuration.GetSurfaceToPose() == BuildSurfaceEnum.GROUND;
        }
    }
}