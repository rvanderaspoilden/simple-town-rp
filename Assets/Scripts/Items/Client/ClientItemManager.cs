using System.Collections;
using System.Collections.Generic;
using Mirror;
using Sim;
using UnityEngine;

/// <summary>
/// Client-side item manager.
/// Receives S2C item packets and drives ItemBehaviour lifecycle: spawn, attach, detach, destroy.
/// Plain C# singleton — no MonoBehaviour, no Mirror lifecycle hooks.
/// </summary>
public class ClientItemManager
{
    private static ClientItemManager _instance;
    public static ClientItemManager Instance => _instance ??= new ClientItemManager();

    // entityId → client-side ItemBehaviour
    private readonly Dictionary<int, ItemBehaviour> _items = new Dictionary<int, ItemBehaviour>();

    private ClientItemManager() { }

    // ── Handler registration ───────────────────────────────────────────────────

    public void RegisterHandlers()
    {
        NetworkClient.RegisterHandler<S2C_SpawnItem>(OnSpawnItem);
        NetworkClient.RegisterHandler<S2C_DestroyItem>(OnDestroyItem);
        NetworkClient.RegisterHandler<S2C_PickupResult>(OnPickupResult);
        NetworkClient.RegisterHandler<S2C_ItemAttachedToHand>(OnItemAttachedToHand);
        NetworkClient.RegisterHandler<S2C_ItemDetachedFromHand>(OnItemDetachedFromHand);
        NetworkClient.RegisterHandler<S2C_DropResult>(OnDropResult);

        NetworkClient.RegisterHandler<S2C_ContainerOpened>(OnContainerOpened);
        NetworkClient.RegisterHandler<S2C_ContainerOpenFailed>(OnContainerOpenFailed);
        NetworkClient.RegisterHandler<S2C_MoveItemResult>(OnMoveItemResult);
        NetworkClient.RegisterHandler<S2C_PocketSync>(OnPocketSync);
    }

    public void UnregisterHandlers()
    {
        NetworkClient.UnregisterHandler<S2C_SpawnItem>();
        NetworkClient.UnregisterHandler<S2C_DestroyItem>();
        NetworkClient.UnregisterHandler<S2C_PickupResult>();
        NetworkClient.UnregisterHandler<S2C_ItemAttachedToHand>();
        NetworkClient.UnregisterHandler<S2C_ItemDetachedFromHand>();
        NetworkClient.UnregisterHandler<S2C_DropResult>();
        NetworkClient.UnregisterHandler<S2C_ContainerOpened>();
        NetworkClient.UnregisterHandler<S2C_ContainerOpenFailed>();
        NetworkClient.UnregisterHandler<S2C_MoveItemResult>();
        NetworkClient.UnregisterHandler<S2C_PocketSync>();
    }

    // ── Container events (consumed par ContainerPanelUI) ──────────────────────

    public static event System.Action<S2C_ContainerOpened> ContainerOpened;
    public static event System.Action<S2C_ContainerOpenFailed> ContainerOpenFailed;
    public static event System.Action<S2C_MoveItemResult> MoveItemResult;
    public static event System.Action<S2C_PocketSync> PocketSync;

    /// <summary>
    /// Cache du dernier snapshot poche reçu. Permet à InventoryUI de re-peupler
    /// les slots poche à chaque OnEnable sans demander au serveur un re-sync.
    /// </summary>
    public static S2C_PocketSync? LastPocketSnapshot { get; private set; }

    private void OnContainerOpened(S2C_ContainerOpened msg) {
        Debug.Log($"[Container] Opened propId={msg.PropId} placeId={msg.PlaceId} slots={msg.SlotCount} items={msg.Items?.Length}");
        ContainerOpened?.Invoke(msg);
    }

    private void OnContainerOpenFailed(S2C_ContainerOpenFailed msg) {
        Debug.LogWarning($"[Container] Open failed propId={msg.PropId} reason={msg.ErrorMessage}");
        ContainerOpenFailed?.Invoke(msg);
    }

    private void OnMoveItemResult(S2C_MoveItemResult msg) {
        if (!msg.Success) Debug.LogWarning($"[Container] Move failed entity={msg.EntityId} reason={msg.ErrorMessage}");
        MoveItemResult?.Invoke(msg);
    }

    private void OnPocketSync(S2C_PocketSync msg) {
        Debug.Log($"[Pocket] Sync placeId={msg.PlaceId} slots={msg.SlotCount} items={msg.Items?.Length}");
        LastPocketSnapshot = msg;
        PocketSync?.Invoke(msg);
    }

    public void Reset()
    {
        foreach (var behaviour in _items.Values)
        {
            if (behaviour != null)
                Object.Destroy(behaviour.gameObject);
        }
        _items.Clear();
        LastPocketSnapshot = null;
        _instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public ItemBehaviour GetItem(int entityId)
    {
        _items.TryGetValue(entityId, out var b);
        return b;
    }

    // ── S2C handlers ──────────────────────────────────────────────────────────

    private void OnSpawnItem(S2C_SpawnItem msg)
    {
        Debug.Log($"[Item] Received spawn entity={msg.EntityId} configId={msg.ItemConfigId} room={msg.RoomId}");

        if (_items.ContainsKey(msg.EntityId))
        {
            Debug.LogWarning($"[ClientItemManager] Duplicate spawn entity={msg.EntityId}, ignoring");
            return;
        }

        GameObject prefab = DatabaseManager.GetItemPrefab(msg.ItemConfigId);
        if (prefab == null)
        {
            Debug.LogError($"[ClientItemManager] No prefab for itemConfigId={msg.ItemConfigId}");
            return;
        }

        GameObject go = Object.Instantiate(prefab, msg.Position, msg.Rotation);

        var identity = go.GetComponent<ItemIdentity>();
        if (identity == null) identity = go.AddComponent<ItemIdentity>();
        identity.Assign(msg.EntityId, msg.RoomId);

        var behaviour = go.GetComponent<ItemBehaviour>();
        if (behaviour == null)
        {
            Debug.LogError($"[ClientItemManager] Prefab for configId={msg.ItemConfigId} has no ItemBehaviour");
            Object.Destroy(go);
            return;
        }

        ItemConfig config = DatabaseManager.ItemConfigs.Find(x => x.ID == msg.ItemConfigId);
        if (config == null)
        {
            Debug.LogError($"[ClientItemManager] ItemConfig not found for id={msg.ItemConfigId}");
            Object.Destroy(go);
            return;
        }

        behaviour.Initialize(config);
        _items[msg.EntityId] = behaviour;

        // If the item is already held (late-join snapshot), attach immediately
        if (msg.IsHeld)
        {
            NetworkManager.singleton.StartCoroutine(
                AttachToHandCoroutine(msg.EntityId, msg.HolderNetId, msg.HolderHand,
                    msg.LocalPosition, msg.LocalRotation));
        }
    }

    private void OnDestroyItem(S2C_DestroyItem msg)
    {
        Debug.Log($"[Item] Received destroy entity={msg.EntityId} room={msg.RoomId}");

        if (!_items.TryGetValue(msg.EntityId, out var behaviour)) return;

        _items.Remove(msg.EntityId);

        // Libère la main du joueur (local OU distant) qui tenait l'entité,
        // sinon le hand state resterait bloqué sur un entityId fantôme et la
        // pose d'animation ne reviendrait pas à NONE.
        ClearHoldingPlayer(msg.EntityId);

        if (behaviour != null)
            Object.Destroy(behaviour.gameObject);
    }

    private void OnPickupResult(S2C_PickupResult msg)
    {
        if (msg.Success) return; // Visual outcome arrives via S2C_ItemAttachedToHand

        Debug.LogWarning($"[Item] Pickup rejected entity={msg.EntityId}: {msg.ErrorMessage}");

        // Feedback d'action banal → toast au-dessus du joueur (ex. mains pleines).
        string toast = MapPickupError(msg.ErrorMessage);
        if (!string.IsNullOrEmpty(toast)) WorldToastManager.Show(toast);
    }

    /// <summary>Traduit les motifs de rejet de pickup pertinents pour le joueur (sinon null = pas de toast).</summary>
    private static string MapPickupError(string error)
    {
        if (string.IsNullOrEmpty(error)) return null;
        if (error.Contains("Hands full")) return "Mains pleines";
        if (error.Contains("Too far"))    return "Trop loin";
        if (error.Contains("Not yours"))  return "Cet objet ne vous appartient pas";
        return null; // erreurs internes (item introuvable, hors room…) → silencieux
    }

    private void OnItemAttachedToHand(S2C_ItemAttachedToHand msg)
    {
        Debug.Log($"[Item] AttachedToHand entity={msg.EntityId} player={msg.PlayerNetId} hand={msg.HandType}");

        NetworkManager.singleton.StartCoroutine(
            AttachToHandCoroutine(msg.EntityId, msg.PlayerNetId, msg.HandType,
                msg.LocalPosition, msg.LocalRotation));
    }

    private void OnItemDetachedFromHand(S2C_ItemDetachedFromHand msg)
    {
        Debug.Log($"[Item] DetachedFromHand entity={msg.EntityId}");

        if (!_items.TryGetValue(msg.EntityId, out var behaviour) || behaviour == null) return;

        behaviour.transform.SetParent(null, worldPositionStays: false);
        behaviour.transform.SetPositionAndRotation(msg.WorldPosition, msg.WorldRotation);
        behaviour.OnDetachedFromHand();

        // Free the holding player's hand slot (local or remote).
        ClearHoldingPlayer(msg.EntityId);
    }

    private void OnDropResult(S2C_DropResult msg)
    {
        if (!msg.Success)
            Debug.LogWarning($"[Item] Drop rejected hand={msg.Hand}: {msg.ErrorMessage}");
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Waits for both the item behaviour and the target player to be available,
    /// then parents the item to the correct hand transform.
    /// </summary>
    private IEnumerator AttachToHandCoroutine(int entityId, uint playerNetId, HandType hand,
        Vector3 localPos, Quaternion localRot)
    {
        float timeout = Time.time + 10f;

        // Wait for item behaviour
        while (!_items.ContainsKey(entityId) && Time.time < timeout)
            yield return null;

        if (!_items.TryGetValue(entityId, out var behaviour) || behaviour == null)
        {
            Debug.LogWarning($"[ClientItemManager] AttachToHand timeout: item entity={entityId} not found");
            yield break;
        }

        // Wait for player GO
        while (!NetworkClient.spawned.ContainsKey(playerNetId) && Time.time < timeout)
            yield return null;

        if (!NetworkClient.spawned.TryGetValue(playerNetId, out var playerIdentity) || playerIdentity == null)
        {
            Debug.LogWarning($"[ClientItemManager] AttachToHand timeout: player netId={playerNetId} not found");
            yield break;
        }

        var playerHands = playerIdentity.GetComponent<PlayerHands>();
        if (playerHands == null)
        {
            Debug.LogWarning($"[ClientItemManager] Player netId={playerNetId} has no PlayerHands");
            yield break;
        }

        Transform handTransform = playerHands.GetHandTransform(hand);
        if (handTransform == null)
        {
            Debug.LogWarning($"[ClientItemManager] Player netId={playerNetId} hand={hand} transform is null");
            yield break;
        }

        behaviour.transform.SetParent(handTransform, worldPositionStays: false);

        // ItemConfig grip override prevails over the network message values when set —
        // each item declares its own resting pos/rot in the hand bone. Falls back to
        // the message values (legacy / non-configured items) when no override is provided.
        ItemConfig config = behaviour.Configuration;
        if (config != null && config.HasGripOverride)
        {
            Vector3    gripPos = config.GripPosition;
            Quaternion gripRot = Quaternion.Euler(config.GripEuler);

            // Les os de main gauche/droite sont des miroirs l'un de l'autre : appliquer le
            // même euler local donne un rendu inversé sur une main. Le grip est défini pour
            // la main DROITE (référence) ; pour la main gauche on le reflète à travers le
            // plan YZ local (négation de pos.x et miroir de rotation : (x,-y,-z,w)).
            if (hand == HandType.Left)
            {
                gripPos.x = -gripPos.x;
                gripRot   = new Quaternion(gripRot.x, -gripRot.y, -gripRot.z, gripRot.w);
            }

            behaviour.transform.localPosition = gripPos;
            behaviour.transform.localRotation = gripRot;
        }
        else
        {
            behaviour.transform.localPosition = localPos;
            behaviour.transform.localRotation = localRot;
        }

        behaviour.OnAttachedToHand(playerNetId, hand);

        // Update PlayerHands state on the actual holder (local OR remote) so the
        // animator pose driver in PlayerHands.NotifyChanged runs on every instance.
        playerHands.SetHand(hand, behaviour, entityId);
        Debug.Log($"[PlayerHands] netId={playerNetId} hand={hand} entity={entityId}");
    }

    /// <summary>
    /// Scans every spawned PlayerHands and clears whichever slot was holding
    /// the given entityId. Used on detach / destroy events that don't carry the
    /// holder's netId in the payload.
    /// </summary>
    private void ClearHoldingPlayer(int entityId)
    {
        foreach (var kvp in NetworkClient.spawned)
        {
            var identity = kvp.Value;
            if (identity == null) continue;
            var hands = identity.GetComponent<PlayerHands>();
            if (hands == null) continue;

            if (hands.LeftEntityId == entityId)
            {
                hands.ClearHand(HandType.Left);
                return;
            }
            if (hands.RightEntityId == entityId)
            {
                hands.ClearHand(HandType.Right);
                return;
            }
        }
    }
}
