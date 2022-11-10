using System;
using System.Collections.Generic;
using System.Linq;
using Interaction;
using Mirror;
using Network.Messages;
using Sim;
using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

public class BuildArea : NetworkBehaviour, IInteractable {
    [SerializeField]
    private BuildAreaConfig config;

    [SerializeField]
    private GameObject freeStateVisual;

    [SerializeField]
    private GameObject underConstructionVisual;

    [SyncVar(hook = nameof(OnStateChange))]
    private BuildAreaState _state;

    private Action[] _actions;

    private void Awake() {
        this._actions = Array.Empty<Action>();
    }

    public override void OnStartClient() {
        this.SetupActions(this.config.Actions);
        this.RefreshVisual();
    }

    protected virtual void OnDestroy() {
        this.UnSubscribeActions(this._actions);
    }

    private void SetupActions(List<Action> actions) {
        this.UnSubscribeActions(this._actions);
        this._actions = actions.Select(Instantiate).ToArray();
        SubscribeActions(this._actions);
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

    private void DoAction(Action action) {
        Debug.Log("Do action : " + action.Label);

        switch (action.Type) {
            case ActionTypeEnum.BUILD:
                this.OpenBuildPanel();
                break;

            default:
                this.Execute(action);
                break;
        }
    }

    [Server]
    public void ResetState() {
        this._state = BuildAreaState.FREE;
    }

    private void OpenBuildPanel() {
        CmdSetState(BuildAreaState.UNDER_CONSTRUCTION);
        PlayerController.Local.Interact(this);
        DefaultViewUI.Instance.DisplayBuildPanel(true, config, (request) => {
            this.CmdBuild(request);
        }, () => { PlayerController.Local.Idle(); });
    }

    private void RefreshVisual() {
        this.freeStateVisual.SetActive(this._state == BuildAreaState.FREE);
        this.underConstructionVisual.SetActive(this._state == BuildAreaState.UNDER_CONSTRUCTION);
    }

    [Command(requiresAuthority = false)]
    private void CmdSetState(BuildAreaState value, NetworkConnectionToClient sender = null) {
        if (this._state != BuildAreaState.OWNED) { // Prevent to change state with the method 'stopInteraction()'
            this._state = value;
        }
    }

    [ClientCallback]
    private void OnStateChange(BuildAreaState _, BuildAreaState newValue) {
        this.RefreshVisual();

        if (newValue == BuildAreaState.OWNED) {
            DefaultViewUI.Instance.DisplayBuildPanel(false);
        }
    }

    [Command(requiresAuthority = false)]
    private void CmdBuild(CreateBuildingMessage request, NetworkConnectionToClient sender = null) {
        BuildingConfig buildingConfig = this.config.Buildings.Find(x => x.ID == request.buildingId);
        
        if(!buildingConfig) Debug.LogError($"Cannot find building with ID ({request.buildingId}) in the available buildings of this area config");

        BuildingController building = Instantiate(buildingConfig.Prefab, this.transform.position, this.transform.rotation);
        
        NetworkServer.Spawn(building.gameObject, sender);

        building.AttachedArea = this;
        
        building.SetCustomizedMaterialParts(request.customizedMaterialParts);

        this._state = BuildAreaState.OWNED;
    }

    protected virtual void Execute(Action action) {
        throw new NotImplementedException();
    }

    public float GetRange() {
        return this.config.RangeToInteract;
    }

    public bool IsInteractable() {
        return this._state != BuildAreaState.OWNED;
    }

    public Action[] GetActions(bool withPriority = false) {
        foreach (var action in this._actions) {
            if (action.Type == ActionTypeEnum.BUILD) {
                action.IsForbidden = this._state != BuildAreaState.FREE;
            }
        }
        
        return this._actions.ToArray();
    }

    public void StopInteraction() {
        DefaultViewUI.Instance.DisplayBuildPanel(false);
        this.CmdSetState(BuildAreaState.FREE);
    }
}

public enum BuildAreaState : byte {
    FREE,
    UNDER_CONSTRUCTION,
    OWNED
}