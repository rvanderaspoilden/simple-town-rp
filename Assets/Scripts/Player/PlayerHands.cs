using System;
using Mirror;
using Sim;
using UnityEngine;

public class PlayerHands : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Transform leftHandTransform;

    [SerializeField]
    private Transform rightHandTransform;

    [Header("Debug")]
    [SerializeField]
    private Item leftHandItem;

    [SerializeField]
    private Item rightHandItem;

    private void Update() {
        if (leftHandItem && leftHandItem.hasAuthority) {
            leftHandItem.transform.position = this.leftHandTransform.position;
            leftHandItem.transform.rotation = this.leftHandTransform.rotation;
        }

        if (rightHandItem && rightHandItem.hasAuthority) {
            rightHandItem.transform.position = this.rightHandTransform.position;
            rightHandItem.transform.rotation = this.rightHandTransform.rotation;
        }
    }

    public void EquipItem(Item item, HandEnum hand) {
        // TODO
    }

    public void EquipItem(Item item) {
        Transform handTransform = null;

        if (item.Configuration.HandleType == ItemHandleType.TWO_HAND) {
            this.rightHandItem = item;
        } else {
            if (CanHandleItem(item, HandEnum.LEFT_HAND)) {
                this.leftHandItem = item;
            } else if (CanHandleItem(item, HandEnum.RIGHT_HAND)) {
                this.rightHandItem = item;
            }
        }
    }

    public bool TryEquipItem(Item item) {
        if (CanHandleItem(item)) {
            item.CmdSetOwner();
            return true;
        }

        Debug.Log("[TryEquipItem] Cannot equip item");
        return false;
    }

    public void UnEquipHand(HandEnum handEnum) {
        // TODO
    }

    public void UnEquipItem(Item item) {
        if (this.leftHandItem == item) {
            this.leftHandItem = null;
        } else if (this.rightHandItem == item) {
            this.rightHandItem = null;
        } else {
            return;
        }

        item.CmdRemoveOwner();
    }

    public bool CanHandleItem(Item item, HandEnum hand) {
        return item.Configuration.HandleType == ItemHandleType.ONE_HAND && this.GetHandItem(hand) == null;
    }

    public bool CanHandleItem(Item item) {
        if (item.Configuration.HandleType == ItemHandleType.TWO_HAND) {
            return this.GetHandItem(HandEnum.LEFT_HAND) == null && this.GetHandItem(HandEnum.RIGHT_HAND) == null;
        }

        return HasFreeHand();
    }

    public bool HasOnlyOneFreeHand() {
        return LeftHandItem == null ^ RightHandItem == null;
    }

    public bool HasFreeHand() {
        return RightHandItem == null || (LeftHandItem == null && (RightHandItem == null || RightHandItem.Configuration.HandleType == ItemHandleType.ONE_HAND));
    }

    private Item GetHandItem(HandEnum hand) {
        return hand == HandEnum.LEFT_HAND ? this.leftHandItem : this.rightHandItem;
    }

    private Transform GetHandTransform(HandEnum hand) {
        return hand == HandEnum.LEFT_HAND ? this.leftHandTransform : this.rightHandTransform;
    }

    public Item LeftHandItem => leftHandItem;

    public Item RightHandItem => rightHandItem;
}

public enum HandEnum {
    LEFT_HAND,
    RIGHT_HAND
}