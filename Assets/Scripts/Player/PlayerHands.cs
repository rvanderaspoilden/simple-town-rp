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
    [SerializeField, Tooltip("Ancre du dos (sac à dos porté) — enfant du bone poitrine.")]
    private Transform backTransform;

    [Header("Debug — read-only")]
    [SerializeField] private ItemBehaviour leftHandItem;
    [SerializeField] private ItemBehaviour rightHandItem;
    [SerializeField] private int leftEntityId  = -1;
    [SerializeField] private int rightEntityId = -1;
    [SerializeField] private ItemBehaviour backItem;
    [SerializeField] private int backEntityId  = -1;

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
        // Idempotent move : si la même entité occupe déjà un AUTRE slot (main opposée,
        // dos), on l'y efface d'abord — le serveur ne broadcast qu'un attach pour un
        // déplacement de slot, jamais de detach explicite pour la source.
        if (entityId != -1)
        {
            if (hand != HandType.Right && rightEntityId == entityId) { rightHandItem = null; rightEntityId = -1; }
            if (hand != HandType.Left  && leftEntityId  == entityId) { leftHandItem  = null; leftEntityId  = -1; }
            if (hand != HandType.Back  && backEntityId  == entityId) { backItem      = null; backEntityId  = -1; }
        }

        switch (hand)
        {
            case HandType.Left:  leftHandItem  = item; leftEntityId  = entityId; break;
            case HandType.Back:  backItem      = item; backEntityId  = entityId; break;
            default:             rightHandItem = item; rightEntityId = entityId; break;
        }
        NotifyChanged();
    }

    public void ClearHand(HandType hand)
    {
        switch (hand)
        {
            case HandType.Left:  leftHandItem  = null; leftEntityId  = -1; break;
            case HandType.Back:  backItem      = null; backEntityId  = -1; break;
            default:             rightHandItem = null; rightEntityId = -1; break;
        }
        NotifyChanged();
    }

    // ── Query API ─────────────────────────────────────────────────────────
    // Modèle « un item actif » (phase 3) : la main droite est le seul slot logique.
    // La gauche ne sert plus qu'à la présentation (pose des items 2 mains).

    public bool HasFreeHand() => rightEntityId == -1;

    public bool CanHandleItem(ItemHandleType handleType) => rightEntityId == -1;

    /// <summary>True si une main tient un item-conteneur (« colis ») — les items ramassés
    /// peuvent alors y être routés même mains pleines. La capacité réelle est validée serveur.</summary>
    public bool IsHoldingContainer()
    {
        return IsContainer(leftHandItem) || IsContainer(rightHandItem);
    }

    private static bool IsContainer(ItemBehaviour item) =>
        item != null && Sim.Scriptables.ItemContainerConfig.Of(item.Configuration)?.IsContainer == true;

    /// <summary>L'item-conteneur (« colis ») actuellement tenu, ou null. Sert à l'affichage
    /// automatique de la grille du conteneur dès qu'un colis est en main.</summary>
    public ItemBehaviour HeldContainerItem
    {
        get
        {
            if (IsContainer(rightHandItem)) return rightHandItem;
            if (IsContainer(leftHandItem))  return leftHandItem;
            return null;
        }
    }

    /// <summary>Conteneur PORTÉ sur le dos (sac à dos), ou null. Sa grille s'affiche
    /// avec l'inventaire quand aucun colis n'est tenu en main.</summary>
    public ItemBehaviour WornContainerItem => IsContainer(backItem) ? backItem : null;

    /// <summary>True si le joueur tient un colis dont la config accepte les meubles (props).
    /// Gate client de l'action « Emballer » ; la place réelle est validée serveur.</summary>
    public bool HoldsPropAcceptingContainer
    {
        get
        {
            ItemBehaviour colis = HeldContainerItem;
            return colis != null
                && Sim.Scriptables.ItemContainerConfig.Of(colis.Configuration)?.AcceptsProps == true;
        }
    }

    public Transform GetHandTransform(HandType hand)
    {
        if (hand == HandType.Left) return leftHandTransform;
        if (hand == HandType.Back) return backTransform != null ? backTransform : rightHandTransform;
        return rightHandTransform;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    public ItemBehaviour LeftHandItem  => leftHandItem;
    public ItemBehaviour RightHandItem => rightHandItem;
    public ItemBehaviour BackItem      => backItem;
    public int           LeftEntityId  => leftEntityId;
    public int           RightEntityId => rightEntityId;
    public int           BackEntityId  => backEntityId;

    // ── Private helpers ───────────────────────────────────────────────────────

    private void NotifyChanged()
    {
        // Pose driver runs for every PlayerHands instance (local + remotes) so
        // remote players display the correct carry animation as well. Per-arm
        // composition: each arm picks its own pose, and a 2H item overrides
        // both via the Two Hand layer.
        // Drives renderer visibility AND the hand-pose layers from the current held
        // items + the holder's current action (single source of truth). A 2H item
        // overrides both arms via the Two Hand layer; an action that hides items
        // (e.g. SIT) also releases the hand poses to NONE.
        if (playerAnimator != null)
        {
            playerAnimator.ApplyHeldItemVisibility();
        }

        // UI event is scoped to the local player only (inventory panel listens once).
        if (PlayerController.Local != null && PlayerController.Local.PlayerHands == this)
        {
            Debug.Log("[InventoryUI] Refresh hands display");
            OnHandChanged?.Invoke();
        }
    }
}
