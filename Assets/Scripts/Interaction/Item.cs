using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

[RequireComponent(typeof(NetworkTransform))]
public class Item : NetworkEntity {
    [SerializeField]
    private ItemConfig configuration;
    
    private Action[] _actions;

    [SyncVar]
    private string owner;

    private void Awake() {
        this._actions = Array.Empty<Action>();
    }

    protected virtual void Start() {
        this.SetAsUnEquipped();
    }
    
    protected virtual void OnDestroy() {
        this.UnSubscribeActions(this._actions);
    }

    public void SetAsEquipped() {
        this.SetupActions(this.configuration.EquippedActions);
    }

    public void SetAsUnEquipped() {
        this.SetupActions(this.configuration.UnEquippedActions);
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
        }
    }
    
    public virtual Action[] GetActions() {
        return this._actions;
    }

    public ItemConfig Configuration => configuration;

    public string Owner => owner;

    public bool HasOwner() {
        return this.owner?.Length > 0;
    }

    [Command(requiresAuthority = false)]
    public void CmdSetOwner(NetworkConnectionToClient sender = null) {
        if (sender == null) {
            Debug.Log("[Item] [CmdSetOwner] Cannot set owner because sender is null");
            return;
        }
        
        this.owner = sender.identity.GetComponent<PlayerController>().CharacterData.Id;
        this.netIdentity.AssignClientAuthority(sender);
    }
    
    [Command(requiresAuthority = false)]
    public void CmdRemoveOwner(NetworkConnectionToClient sender = null) {
        this.owner = string.Empty;
        this.netIdentity.RemoveClientAuthority();
    }

    protected virtual void Pick() {
        PlayerController.Local.PlayerHands.EquipItem(this);
    }

    protected virtual void Drop() {
        PlayerController.Local.PlayerHands.UnEquipItem(this);
    }
}