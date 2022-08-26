using System.Collections;
using Mirror;
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

    [SyncVar(hook = nameof(OnLeftItemChanged))]
    [SerializeField]
    private uint leftHandItemId;

    [SyncVar(hook = nameof(OnRightItemChanged))]
    [SerializeField]
    private uint rightHandItemId;


    public void EquipItem(Item item, HandEnum hand) {
        // TODO
    }

    public void Swap() {
        Debug.Log("SWAP");
        CmdSwap();
    }

    [Command]
    public void CmdSwap() {
        (this.leftHandItemId, this.rightHandItemId) = (this.rightHandItemId, this.leftHandItemId);
    }

    public void EquipItem(Item item) {
        CmdEquipItem(item.netId);
    }

    [Command]
    public void CmdEquipItem(uint itemNetId) {
        Item item = NetworkIdentity.spawned[itemNetId].gameObject.GetComponent<Item>();

        ItemConfig config = item.Configuration;

        if (config.HandleType == ItemHandleType.TWO_HAND) {
            this.rightHandItemId = itemNetId;
        } else {
            if (CanHandleItem(config.HandleType, HandEnum.LEFT_HAND)) {
                this.leftHandItemId = itemNetId;
            } else if (CanHandleItem(config.HandleType, HandEnum.RIGHT_HAND)) {
                this.rightHandItemId = itemNetId;
            }
        }

        item.netIdentity.AssignClientAuthority(this.connectionToClient);
        item.ChangeIsEquippedState(true);
    }

    private void OnLeftItemChanged(uint oldValue, uint newValue) {
        Debug.Log($"Left Item changed with ID : {newValue}");

        StartCoroutine(this.CheckHand(HandEnum.LEFT_HAND));
    }

    private void OnRightItemChanged(uint oldValue, uint newValue) {
        Debug.Log($"Right Item changed with ID : {newValue}");

        StartCoroutine(this.CheckHand(HandEnum.RIGHT_HAND));
    }

    private IEnumerator CheckHand(HandEnum hand) {
        uint handItemNetId = this.rightHandItemId;
        Transform handTransform = this.rightHandTransform;

        if (hand == HandEnum.LEFT_HAND) {
            handItemNetId = this.leftHandItemId;
            handTransform = this.leftHandTransform;
        }

        float time = Time.time;

        while (handItemNetId != 0 && !NetworkIdentity.spawned.ContainsKey(handItemNetId) && Time.time < time + 10f) {
            yield return new WaitForSeconds(.1f);
        }

        if (Time.time > time + 10f || handItemNetId == 0) {
            Debug.Log($"[OnLeftItemChanged] [TIMEOUT] item not found with netId {handItemNetId}");
            
            if (hand == HandEnum.LEFT_HAND) {
                this.leftHandItem = null;
            } else {
                this.rightHandItem = null;
            }
            
            yield break;
        }

        if (hand == HandEnum.LEFT_HAND) {
            this.leftHandItem = NetworkIdentity.spawned[handItemNetId].gameObject.GetComponent<Item>();
            this.SetItemParent(leftHandItem, handTransform);
        } else {
            this.rightHandItem = NetworkIdentity.spawned[handItemNetId].gameObject.GetComponent<Item>();
            this.SetItemParent(rightHandItem, handTransform);
        }
    }

    private void SetItemParent(Item item, Transform parent) {
        var itemTransform = item.transform;
        itemTransform.position = parent.position;
        itemTransform.rotation = parent.rotation;
        itemTransform.parent = parent;
    }

    public bool TryEquipItem(Item item) {
        if (CanHandleItem(item)) {
            this.EquipItem(item);
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
        }
    }

    public bool CanHandleItem(Item item, HandEnum hand) {
        return item.Configuration.HandleType == ItemHandleType.ONE_HAND && this.GetHandItem(hand) == null;
    }

    public bool CanHandleItem(ItemHandleType itemHandleType, HandEnum hand) {
        return itemHandleType == ItemHandleType.ONE_HAND && this.GetHandItem(hand) == null;
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