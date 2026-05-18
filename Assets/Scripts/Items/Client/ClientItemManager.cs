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
    }

    public void UnregisterHandlers()
    {
        NetworkClient.UnregisterHandler<S2C_SpawnItem>();
        NetworkClient.UnregisterHandler<S2C_DestroyItem>();
        NetworkClient.UnregisterHandler<S2C_PickupResult>();
        NetworkClient.UnregisterHandler<S2C_ItemAttachedToHand>();
        NetworkClient.UnregisterHandler<S2C_ItemDetachedFromHand>();
        NetworkClient.UnregisterHandler<S2C_DropResult>();
    }

    public void Reset()
    {
        foreach (var behaviour in _items.Values)
        {
            if (behaviour != null)
                Object.Destroy(behaviour.gameObject);
        }
        _items.Clear();
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

        // Si l'item était tenu par le joueur local, libérer la main (sinon
        // hand state reste bloqué sur un entityId fantôme — bug "mains pleines").
        UpdateLocalPlayerHands(msg.EntityId, clearHand: true, hand: default);

        if (behaviour != null)
            Object.Destroy(behaviour.gameObject);
    }

    private void OnPickupResult(S2C_PickupResult msg)
    {
        if (!msg.Success)
        {
            Debug.LogWarning($"[Item] Pickup rejected entity={msg.EntityId}: {msg.ErrorMessage}");
            return;
        }
        // Visual outcome arrives via S2C_ItemAttachedToHand — nothing else needed here
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

        // Update local PlayerHands if this player holds it
        UpdateLocalPlayerHands(msg.EntityId, clearHand: true, hand: default);
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
        behaviour.transform.localPosition = localPos;
        behaviour.transform.localRotation = localRot;
        behaviour.OnAttachedToHand(playerNetId, hand);

        // Update local PlayerHands UI state
        UpdateLocalPlayerHands(entityId, clearHand: false, hand: hand, behaviour: behaviour, playerNetId: playerNetId);
    }

    private void UpdateLocalPlayerHands(int entityId, bool clearHand, HandType hand,
        ItemBehaviour behaviour = null, uint playerNetId = 0)
    {
        if (PlayerController.Local == null) return;
        var localNetId = NetworkClient.connection?.identity?.netId ?? 0;

        if (clearHand)
        {
            // Find which hand held this entity
            var hands = PlayerController.Local.PlayerHands;
            if (hands.LeftEntityId == entityId)
                hands.ClearHand(HandType.Left);
            else if (hands.RightEntityId == entityId)
                hands.ClearHand(HandType.Right);
        }
        else if (playerNetId == localNetId)
        {
            Debug.Log($"[PlayerHands] Updated {hand} hand with entity={entityId}");
            PlayerController.Local.PlayerHands.SetHand(hand, behaviour, entityId);
        }
    }
}
