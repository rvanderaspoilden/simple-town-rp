using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Enums;
using Sim.Utils;
using UnityEngine;
using Action = Sim.Interactables.Action;

public class Item : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    protected ItemConfig configuration;

    [Header("Debug")]
    [SerializeField]
    [SyncVar]
    private uint playerNetIdBind;

    [SerializeField]
    [SyncVar]
    private HandEnum handBind;

    private PlayerController playerBind;

    private Action[] _actions;

    private void Awake() {
        this._actions = Array.Empty<Action>();
    }

    public override void OnStartClient() {
        if (playerNetIdBind != 0) {
            this.Equip(this.playerNetIdBind, this.handBind);
        } else {
            this.SetupActions(this.configuration.UnEquippedActions);
        }
    }

    protected virtual void OnDestroy() {
        this.UnSubscribeActions(this._actions);
    }

    [Server]
    public void Bind(uint playerNetId, HandEnum hand) {
        this.playerNetIdBind = playerNetId;
        this.handBind = hand;
        this.Equip(playerNetId, hand);
        this.RpcBind(this.playerNetIdBind, this.handBind);
    }

    [ClientRpc]
    public void RpcBind(uint playerNetId, HandEnum hand) {
        this.Equip(playerNetId, hand);
    }

    private void Equip(uint playerNetId, HandEnum hand) {
        this.playerBind = NetworkUtils.FindObject(playerNetId).GetComponent<PlayerController>();
        
        Transform handTransform = this.playerBind.PlayerHands.GetHandTransform(hand);
        var itemTransform = transform;
        itemTransform.position = handTransform.position;
        itemTransform.rotation = handTransform.rotation;
        itemTransform.parent = handTransform;
        
        this.SetupActions(this.configuration.EquippedActions);
    }

    [Server]
    public void UnBind() {
        this.playerNetIdBind = 0;
        this.UnEquip();
        this.RpcUnBind();
    }

    [ClientRpc]
    public void RpcUnBind() {
        this.UnEquip();
        this.SetupActions(this.configuration.UnEquippedActions);
    }

    public void UnEquip() {
        this.playerBind = null;
        this.transform.parent = null;
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