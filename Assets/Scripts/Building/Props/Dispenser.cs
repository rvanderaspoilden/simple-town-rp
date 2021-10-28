using System.Linq;
using Mirror;
using Sim;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using Sim.UI;
using UnityEngine;

public class Dispenser : Props {
    [Header("Settings")]
    [SerializeField]
    private AudioClip useSound;

    protected override void Execute(Action action) {
        if (action.Type.Equals(ActionTypeEnum.USE)) {
            this.OpenDispenser();
        }
    }

    [Command(requiresAuthority = false)]
    public void CmdBuyItem(int itemIdx, NetworkConnectionToClient sender = null) {
        Debug.Log($"[Dispenser] Player {sender.identity.gameObject.name} wants to buy item with ID {itemIdx}");
        PlayerBankAccount playerBankAccount = sender.identity.gameObject.GetComponent<PlayerBankAccount>();
        ItemPrice itemPrice = ((DispenserConfiguration) this.configuration).ItemsToSell.Find(x => x.item.ID == itemIdx);

        if (playerBankAccount.Money >= itemPrice.price) {
            playerBankAccount.TakeMoney(itemPrice.price);

            Item item = Instantiate(itemPrice.item.Prefab, sender.identity.gameObject.transform.position, Quaternion.identity);
            NetworkServer.Spawn(item.gameObject, sender);
            
            this.TargetItemBought(sender, item.netId);
            this.RpcItemBought();
        } else {
            Debug.Log("[Dispenser] Player has not enough money to buy item");
        }
    }

    [ClientRpc]
    public void RpcItemBought() {
        this.audioSource.PlayOneShot(this.useSound);
    }

    [TargetRpc]
    public void TargetItemBought(NetworkConnection conn, uint itemNetId) {
        if (NetworkIdentity.spawned.ContainsKey(itemNetId)) {
            PlayerController.Local.PlayerHands.TryEquipItem(NetworkIdentity.spawned[itemNetId].GetComponent<Item>());
            
            if (PlayerController.Local.PlayerHands.HasOnlyOneFreeHand()) {
                this.StopInteraction();
            }
        } else {
            Debug.LogError("[TargetItemBought] Cannot find spawned item");
        }
    }

    public void OpenDispenser() {
        PlayerController.Local.Interact(this);
        DefaultViewUI.Instance.ShowPropsContentUI(this);
    }

    public override Action[] GetActions(bool withPriority = false) {
        foreach (var action in this.actions) {
            if (action.Type == ActionTypeEnum.USE) {
                action.IsForbidden = !PlayerController.Local.PlayerHands.HasFreeHand();
            }
        }

        return this.actions;
    }

    public override void StopInteraction() {
        DefaultViewUI.Instance.HidePropsContentUI();
    }

    public void BuyItem(ItemConfig itemConfig) {
        this.CmdBuyItem(itemConfig.ID);
    }
}
