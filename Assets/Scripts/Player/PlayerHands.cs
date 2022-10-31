using System.Collections;
using Mirror;
using UnityEngine;

/**
 * This class is used to manage hands
 *
 * Equipment system
 */
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
    private uint leftHandItemNetId;

    [SyncVar(hook = nameof(OnRightItemChanged))]
    [SerializeField]
    private uint rightHandItemNetId;

    public delegate void HandChanged();

    public static event HandChanged OnHandChanged;

    public void Swap() {
        Debug.Log("SWAP");
        CmdSwap();
    }

    [Command]
    public void CmdSwap() {
        uint currentLeftHandItemNetId = this.leftHandItemNetId;
        uint currentRightHandItemNetId = this.rightHandItemNetId;

        if (currentLeftHandItemNetId > 0 && this.leftHandItem) {
            this.leftHandItem.Bind(this.netId, HandEnum.RIGHT_HAND);
        }

        if (currentRightHandItemNetId > 0 && this.rightHandItem) {
            this.rightHandItem.Bind(this.netId, HandEnum.LEFT_HAND);
        }

        (this.leftHandItemNetId, this.rightHandItemNetId) = (this.rightHandItemNetId, this.leftHandItemNetId);
        (this.leftHandItem, this.rightHandItem) = (this.rightHandItem, this.leftHandItem);
    }

    [Command]
    public void CmdEquipItem(uint itemNetId) {
        Item item = NetworkIdentity.spawned[itemNetId].gameObject.GetComponent<Item>();

        ItemConfig config = item.Configuration;

        if (config.HandleType == ItemHandleType.TWO_HAND) {
            this.rightHandItemNetId = itemNetId;
            this.rightHandItem = item;
            item.Bind(this.netId, HandEnum.RIGHT_HAND);
        } else {
            if (CanHandleItem(config.HandleType, HandEnum.LEFT_HAND)) {
                this.leftHandItemNetId = itemNetId;
                this.leftHandItem = item;
                item.Bind(this.netId, HandEnum.LEFT_HAND);
            } else if (CanHandleItem(config.HandleType, HandEnum.RIGHT_HAND)) {
                this.rightHandItemNetId = itemNetId;
                this.rightHandItem = item;
                item.Bind(this.netId, HandEnum.RIGHT_HAND);
            }
        }
    }

    private void OnLeftItemChanged(uint oldValue, uint newValue) {
        Debug.Log($"Left Item changed with ID : {newValue}");

        StartCoroutine(this.CheckHand(HandEnum.LEFT_HAND, oldValue, newValue));
    }

    private void OnRightItemChanged(uint oldValue, uint newValue) {
        Debug.Log($"Right Item changed with ID : {newValue}");

        StartCoroutine(this.CheckHand(HandEnum.RIGHT_HAND, oldValue, newValue));
    }

    private IEnumerator CheckHand(HandEnum hand, uint oldItemNetId, uint newItemNetId) {
        if (newItemNetId == 0) {
            if (hand == HandEnum.RIGHT_HAND) {
                this.rightHandItem = null;
            } else {
                this.leftHandItem = null;
            }

            // Release case
            if (oldItemNetId != 0) {
                if (isLocalPlayer) {
                    OnHandChanged?.Invoke();
                }
            }

            yield break;
        }

        float time = Time.time;

        while (!NetworkIdentity.spawned.ContainsKey(newItemNetId) && Time.time < time + 10f) {
            yield return new WaitForSeconds(.1f);
        }

        if (Time.time > time + 10f) {
            Debug.Log($"[OnLeftItemChanged] [TIMEOUT] item not found with netId {newItemNetId}");
        }

        if (hand == HandEnum.LEFT_HAND) {
            this.leftHandItem = NetworkIdentity.spawned[newItemNetId].gameObject.GetComponent<Item>();
        } else {
            this.rightHandItem = NetworkIdentity.spawned[newItemNetId].gameObject.GetComponent<Item>();
        }

        if (isLocalPlayer) {
            OnHandChanged?.Invoke();
        }
    }

    public void TryEquipItem(Item item) {
        if (!CanHandleItem(item)) return;

        this.CmdEquipItem(item.netId);
    }

    public void UnEquipItem(Item item) {
        this.CmdUnEquipItem(item.netId);
    }

    [Command]
    public void CmdUnEquipItem(uint itemNetId) {
        Item item = NetworkIdentity.spawned[itemNetId].gameObject.GetComponent<Item>();

        if (this.leftHandItemNetId == itemNetId) {
            this.leftHandItemNetId = 0;
        } else if (this.rightHandItemNetId == itemNetId) {
            this.rightHandItemNetId = 0;
        }

        item.UnBind();
    }

    [Server]
    public void UnEquipAndDestroy(uint itemNetId) {
        Item item = NetworkIdentity.spawned[itemNetId].gameObject.GetComponent<Item>();

        if (this.leftHandItemNetId == itemNetId) {
            this.leftHandItemNetId = 0;
        } else if (this.rightHandItemNetId == itemNetId) {
            this.rightHandItemNetId = 0;
        }

        NetworkServer.Destroy(item.gameObject);
    }

    public bool CanHandleItem(Item item, HandEnum hand) {
        return item.Configuration.HandleType == ItemHandleType.ONE_HAND && this.GetHandItemNetId(hand) == 0;
    }

    public bool CanHandleItem(ItemHandleType itemHandleType, HandEnum hand) {
        return itemHandleType == ItemHandleType.ONE_HAND && this.GetHandItemNetId(hand) == 0;
    }

    public bool CanHandleItem(Item item) {
        if (item.Configuration.HandleType == ItemHandleType.TWO_HAND) {
            return this.GetHandItemNetId(HandEnum.LEFT_HAND) == 0 && this.GetHandItemNetId(HandEnum.RIGHT_HAND) == 0;
        }

        return HasFreeHand();
    }

    public bool HasOnlyOneFreeHand() {
        return LeftHandItem == null ^ RightHandItem == null;
    }

    public bool HasFreeHand() {
        return rightHandItemNetId == 0 || (leftHandItemNetId == 0 && (rightHandItemNetId == 0 || RightHandItem.Configuration.HandleType == ItemHandleType.ONE_HAND));
    }

    private Item GetHandItem(HandEnum hand) {
        return hand == HandEnum.LEFT_HAND ? this.leftHandItem : this.rightHandItem;
    }

    private uint GetHandItemNetId(HandEnum hand) {
        return hand == HandEnum.LEFT_HAND ? this.leftHandItemNetId : this.rightHandItemNetId;
    }

    public Transform GetHandTransform(HandEnum hand) {
        return hand == HandEnum.LEFT_HAND ? this.leftHandTransform : this.rightHandTransform;
    }

    public Item LeftHandItem => leftHandItem;

    public Item RightHandItem => rightHandItem;
}

public enum HandEnum {
    LEFT_HAND,
    RIGHT_HAND
}