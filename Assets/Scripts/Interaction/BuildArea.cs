using System;
using System.Collections.Generic;
using System.Linq;
using Interaction;
using Mirror;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

public class BuildArea : NetworkBehaviour, IInteractable {
    [SerializeField]
    private BuildAreaConfig config;

    private Action[] _actions;

    private void Awake() {
        this._actions = Array.Empty<Action>();
    }

    public override void OnStartClient() {
        this.SetupActions(this.config.Actions);
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
                this.Build();
                break;

            default:
                this.Execute(action);
                break;
        }
    }

    private void Build() { }

    protected virtual void Execute(Action action) {
        throw new NotImplementedException();
    }

    public float GetRange() {
        return this.config.RangeToInteract;
    }

    public bool IsInteractable() {
        return true;
    }

    public Action[] GetActions(bool withPriority = false) {
        return this.config.Actions.ToArray();
    }
}