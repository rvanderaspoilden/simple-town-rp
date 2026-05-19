using Mirror;
using Sim;
using UnityEngine;

/// <summary>
/// Tracks hand state for one player.
/// Purely data-driven — no SyncVar, no Command, no Mirror lifecycle.
/// State is written by ClientItemManager when S2C_ItemAttachedToHand / S2C_ItemDetachedFromHand arrive.
/// Requests (pickup, drop, swap) go via NetworkClient.Send in ItemBehaviour and RequestXxx methods.
/// </summary>
public class PlayerHands : MonoBehaviour
{
    [Header("Hand transforms (assign in prefab)")]
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private Transform rightHandTransform;

    [Header("Debug — read-only")]
    [SerializeField] private ItemBehaviour leftHandItem;
    [SerializeField] private ItemBehaviour rightHandItem;
    [SerializeField] private int leftEntityId  = -1;
    [SerializeField] private int rightEntityId = -1;

    private PlayerAnimator playerAnimator;

    public delegate void HandChanged();
    public static event HandChanged OnHandChanged;

    private void Awake()
    {
        playerAnimator = GetComponent<PlayerAnimator>();
    }

    // ── State API (called by ClientItemManager) ───────────────────────────────

    public void SetHand(HandType hand, ItemBehaviour item, int entityId)
    {
        // Idempotent move: if the same entity is already in the OTHER hand,
        // clear it there first. This handles the "swap with one empty hand"
        // case where the server only broadcasts a single attach packet and
        // never an explicit detach for the source hand.
        if (entityId != -1)
        {
            if (hand == HandType.Left && rightEntityId == entityId)
            {
                Debug.Log($"[PlayerHands] Moving entity={entityId} Right -> Left, clearing source slot");
                rightHandItem = null;
                rightEntityId = -1;
            }
            else if (hand == HandType.Right && leftEntityId == entityId)
            {
                Debug.Log($"[PlayerHands] Moving entity={entityId} Left -> Right, clearing source slot");
                leftHandItem = null;
                leftEntityId = -1;
            }
        }

        if (hand == HandType.Left)
        {
            leftHandItem  = item;
            leftEntityId  = entityId;
        }
        else
        {
            rightHandItem  = item;
            rightEntityId  = entityId;
        }
        NotifyChanged();
    }

    public void ClearHand(HandType hand)
    {
        if (hand == HandType.Left)
        {
            leftHandItem = null;
            leftEntityId = -1;
        }
        else
        {
            rightHandItem = null;
            rightEntityId = -1;
        }
        NotifyChanged();
    }

    // ── Request API (fire-and-forget; server is authoritative) ────────────────

    public void RequestSwap()
    {
        // Client-side prediction: swap local state immediately for instant UI feedback.
        // The server is authoritative — its S2C_ItemAttachedToHand broadcasts will
        // overwrite this state on arrival (no-op in the typical case, corrective on divergence).
        Debug.Log($"[InventoryUI] Local prediction swap Right={rightEntityId} <-> Left={leftEntityId}");
        (leftHandItem, rightHandItem)   = (rightHandItem, leftHandItem);
        (leftEntityId, rightEntityId)   = (rightEntityId, leftEntityId);
        NotifyChanged();

        NetworkClient.Send(new C2S_RequestSwapHands());
    }

    // Convenience alias kept for InventoryUI call-site compatibility
    public void Swap() => RequestSwap();

    // ── Query API ─────────────────────────────────────────────────────────────

    public bool HasFreeHand()
    {
        return rightEntityId == -1
            || (leftEntityId == -1
                && (rightHandItem == null
                    || rightHandItem.Configuration.HandleType == ItemHandleType.ONE_HAND));
    }

    public bool CanHandleItem(ItemHandleType handleType)
    {
        if (handleType == ItemHandleType.TWO_HAND)
            return leftEntityId == -1 && rightEntityId == -1;
        return HasFreeHand();
    }

    public Transform GetHandTransform(HandType hand)
    {
        return hand == HandType.Left ? leftHandTransform : rightHandTransform;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    public ItemBehaviour LeftHandItem  => leftHandItem;
    public ItemBehaviour RightHandItem => rightHandItem;
    public int           LeftEntityId  => leftEntityId;
    public int           RightEntityId => rightEntityId;

    // ── Private helpers ───────────────────────────────────────────────────────

    private void NotifyChanged()
    {
        // Pose driver runs for every PlayerHands instance (local + remotes) so
        // remote players display the correct carry animation as well. Per-arm
        // composition: each arm picks its own pose, and a 2H item overrides
        // both via the Two Hand layer.
        if (playerAnimator != null)
        {
            ResolvedPose pose = HandPoseResolver.Resolve(leftHandItem, rightHandItem);
            playerAnimator.SetRightHandPose(pose.Right);
            playerAnimator.SetLeftHandPose(pose.Left);
            playerAnimator.SetTwoHandPose(pose.TwoHand);
        }

        // UI event is scoped to the local player only (inventory panel listens once).
        if (PlayerController.Local != null && PlayerController.Local.PlayerHands == this)
        {
            Debug.Log("[InventoryUI] Refresh hands display");
            OnHandChanged?.Invoke();
        }
    }
}
