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

    public void EquipItem(Item item, HandEnum hand) {
        // TODO
    }
    
    public void EquipItem(Item item) {
        Transform currentItemTransform = item.transform;
        Transform handTransform = null;
        
        if (CanHandleItem(item, HandEnum.LEFT_HAND)) {
            this.leftHandItem = item;
            handTransform = GetHandTransform(HandEnum.LEFT_HAND);
        } else if (CanHandleItem(item, HandEnum.RIGHT_HAND)) {
            this.rightHandItem = item;
            handTransform = GetHandTransform(HandEnum.RIGHT_HAND);
        } else {
            Debug.LogError("[PlayerHands] [EquipItem] Cannot equip item");
            return;
        }

        currentItemTransform.rotation = handTransform.rotation;
        currentItemTransform.position = handTransform.position;
        currentItemTransform.SetParent(handTransform);

        item.CmdSetOwner();
    }

    public void UnEquipHand(HandEnum handEnum) {
        // TODO
    }

    public bool CanHandleItem(Item item, HandEnum hand) {
        return item.Configuration.HandleType == ItemHandleType.ONE_HAND && this.GetHandItem(hand) == null;
    }

    public bool CanHandleItem(Item item) {
        if (item.Configuration.HandleType == ItemHandleType.TWO_HAND) {
            return this.GetHandItem(HandEnum.LEFT_HAND) == null && this.GetHandItem(HandEnum.RIGHT_HAND) == null;
        }

        return this.GetHandItem(HandEnum.LEFT_HAND) == null || this.GetHandItem(HandEnum.RIGHT_HAND) == null;
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