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
    [Header("Settings")]
    [SerializeField]
    protected ItemConfig configuration;

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
        this.RpcSetOwner(sender);
    }

    [TargetRpc]
    public void RpcSetOwner(NetworkConnection conn) {
        this.SetAsEquipped();
        PlayerController.Local.PlayerHands.EquipItem(this);
        Debug.Log("I'm now the owner of this item");
    }

    [Command(requiresAuthority = false)]
    public void CmdRemoveOwner(NetworkConnectionToClient sender = null) {
        this.owner = string.Empty;
        this.netIdentity.RemoveClientAuthority();
        this.RpcRemoveOwner(sender);

        this.transform.rotation = Quaternion.identity;

        RaycastHit hit;

        if (Physics.Raycast(this.transform.position, Vector3.down, out hit, 10)) {
            this.transform.position = hit.point;
        }
    }

    [TargetRpc]
    public void RpcRemoveOwner(NetworkConnection conn) {
        this.SetAsUnEquipped();
        HUDManager.Instance.InventoryUI.CloseCurrentActionMenu();
        HUDManager.Instance.InventoryUI.UpdateUI();
        Debug.Log("I'm no longer the owner of this item");
    }

    [Client]
    protected virtual void Pick() {
        PlayerController.Local.PlayerHands.TryEquipItem(this);
    }

    [Client]
    protected virtual void Drop() {
        PlayerController.Local.PlayerHands.UnEquipItem(this);
    }
}