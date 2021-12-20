using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

public class Item : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    protected ItemConfig configuration;

    [SerializeField]
    [SyncVar(hook = nameof(IsEquippedStateChanged))]
    private bool isEquipped;

    private Action[] _actions;

    private void Awake() {
        this._actions = Array.Empty<Action>();
    }

    protected virtual void Start() {
        this.SetAsUnEquipped();
    }

    protected virtual void OnDestroy() {
        this.UnSubscribeActions(this._actions);
    }

    private void SetAsEquipped() {
        this.SetupActions(this.configuration.EquippedActions);
    }

    private void SetAsUnEquipped() {
        this.SetupActions(this.configuration.UnEquippedActions);
    }
    
    public void IsEquippedStateChanged(bool old, bool newValue) {
        if (newValue) {
            this.SetAsEquipped();
        } else {
            this.SetAsUnEquipped();
        }
    }

    [Server]
    public void ChangeIsEquippedState(bool value) {
        this.isEquipped = value;
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
            case ActionTypeEnum.PICK:
                this.Pick();
                break;

            case ActionTypeEnum.DROP:
                this.Drop();
                break;

            default:
                this.Execute(action);
                break;
        }
    }

    protected virtual void Execute(Action action) {
        throw new NotImplementedException();
    }

    public virtual Action[] GetActions() {
        foreach (var action in this._actions) {
            if (action.Type == ActionTypeEnum.PICK) {
                action.IsForbidden = !PlayerController.Local.PlayerHands.CanHandleItem(this);
            }
        }

        return this._actions;
    }

    public ItemConfig Configuration => configuration;

    protected virtual void Pick() {
        PlayerController.Local.PlayerHands.TryEquipItem(this);
    }

    protected virtual void Drop() {
        PlayerController.Local.PlayerHands.UnEquipItem(this);
    }
}