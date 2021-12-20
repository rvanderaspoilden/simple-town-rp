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

    [SyncVar(hook = nameof(OnLeftItemChanged))]
    [SerializeField]
    private uint leftHandItemId;
    
    [SyncVar(hook = nameof(OnRightItemChanged))]
    [SerializeField]
    private uint rightHandItemId;

    public override void OnStartClient() {
        this.CheckLeftHand();
    }

    public void EquipItem(Item item, HandEnum hand) {
        // TODO
    }

    public void Swap() {
        (this.leftHandItem, this.rightHandItem) = (this.rightHandItem, this.leftHandItem);
    }

    public void EquipItem(Item item) {
        CmdEquipItem(item.netId);
    }

    [Command]
    public void CmdEquipItem(uint itemNetId) {
        Item item = NetworkIdentity.spawned[itemNetId].gameObject.GetComponent<Item>();
        
        ItemConfig config = item.Configuration;

        /*if (config.HandleType == ItemHandleType.TWO_HAND) {
            this.rightHandItemId = config.ID;
        } else {
            if (CanHandleItem(config.HandleType, HandEnum.LEFT_HAND)) {
                this.leftHandItemId = config.ID;
            } else if (CanHandleItem(config.HandleType, HandEnum.RIGHT_HAND)) {
                this.rightHandItemId = config.ID;
            }
        }

        
        NetworkServer.Destroy(item.gameObject);*/
        
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
        
        // TODO: clean previous item
        
        /*ItemConfig itemConfig = DatabaseManager.ItemConfigs.Find(x => x.ID == newValue);
        this.leftHandItem = Instantiate(itemConfig.Prefab);*/

        this.CheckLeftHand();
    }

    private void CheckLeftHand() {
        if (!NetworkIdentity.spawned.ContainsKey(this.leftHandItemId)) {
            Debug.Log($"[OnLeftItemChanged] item not found with netId {this.leftHandItemId}");
            return;
        }
        
        this.leftHandItem = NetworkIdentity.spawned[this.leftHandItemId].gameObject.GetComponent<Item>();

        this.SetItemParent(this.leftHandItem, this.leftHandTransform);
    }
    
    private void OnRightItemChanged(uint oldValue, uint newValue) {
        Debug.Log($"Right Item changed with ID : {newValue}");
    }

    private void SetItemParent(Item item, Transform parent) {
        var parentTransform = parent.transform;
        var itemTransform = item.transform;
        itemTransform.position = parentTransform.position;
        itemTransform.rotation = parentTransform.rotation;
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