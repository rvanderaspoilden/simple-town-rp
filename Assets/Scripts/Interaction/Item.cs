using System;
using System.Linq;
using Mirror;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

[RequireComponent(typeof(NetworkTransform))]
public class Item : NetworkEntity {
    [SerializeField]
    private ItemConfig configuration;
    
    private Action[] actions;
    protected virtual void Start() {
        this.SetupActions();
    }
    
    protected virtual void OnDestroy() {
        this.UnSubscribeActions(this.actions);
    }
    
    private void SetupActions() {
        this.actions = this.configuration.Actions.Select(Instantiate).ToArray();
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
    
    private void DoAction(Action action) {
        Debug.Log("Do action : " + action.Label);

        switch (action.Type) {
            case ActionTypeEnum.PICK:
                this.Pick();
                break;
        }
    }
    
    public virtual Action[] GetActions() {
        return this.actions;
    }

    protected virtual void Pick() {
        throw new NotImplementedException();
    }
}