// All item network messages live in the global namespace.
// Mirror's handler registration is reflection-based — mixing namespaces breaks matching.
using Mirror;
using UnityEngine;

/// <summary>Server → all clients in room: item spawned (or late-join snapshot entry).</summary>
public struct S2C_SpawnItem : NetworkMessage
{
    public int    EntityId;
    public string RoomId;
    public int    ItemConfigId;
    public Vector3    Position;
    public Quaternion Rotation;
    // Held state (populated for snapshot entries of already-held items)
    public bool   IsHeld;
    public uint   HolderNetId;
    public HandType HolderHand;
    public Vector3    LocalPosition;
    public Quaternion LocalRotation;
}

/// <summary>Server → all clients in room: item removed from world.</summary>
public struct S2C_DestroyItem : NetworkMessage
{
    public int    EntityId;
    public string RoomId;
}

/// <summary>Client → server: player wants to pick up an item.</summary>
public struct C2S_RequestPickupItem : NetworkMessage
{
    public int EntityId;
}

/// <summary>Server → requesting client only: result of a pickup attempt.</summary>
public struct S2C_PickupResult : NetworkMessage
{
    public bool   Success;
    public int    EntityId;
    public string ErrorMessage;
}

/// <summary>Server → all clients in room: item attached to a player's hand.</summary>
public struct S2C_ItemAttachedToHand : NetworkMessage
{
    public int    EntityId;
    public uint   PlayerNetId;
    public HandType HandType;
    public Vector3    LocalPosition;
    public Quaternion LocalRotation;
}

/// <summary>Server → all clients in room: item detached from a hand and dropped.</summary>
public struct S2C_ItemDetachedFromHand : NetworkMessage
{
    public int    EntityId;
    public Vector3    WorldPosition;
    public Quaternion WorldRotation;
}

/// <summary>Client → server: player wants to drop the item in the specified hand.</summary>
public struct C2S_RequestDropItem : NetworkMessage
{
    public HandType Hand;
}

/// <summary>Server → requesting client only: result of a drop attempt.</summary>
public struct S2C_DropResult : NetworkMessage
{
    public bool   Success;
    public HandType Hand;
    public string ErrorMessage;
}

/// <summary>Client → server: swap left ↔ right hand items.</summary>
public struct C2S_RequestSwapHands : NetworkMessage { }

/// <summary>Admin/debug: client requests a server-side item spawn at a position.</summary>
public struct C2S_AdminSpawnItem : NetworkMessage
{
    public int     ItemConfigId;
    public Vector3 Position;
}

// ── Storage containers ────────────────────────────────────────────────────────
// Un conteneur (frigo, placard…) = une Place backend identifiée par
// "container:{propUuid}". Les items du conteneur sont stockés en DB (placeId +
// stateData.slotIndex) ; pas de ItemEntity dans _rooms. Quand un joueur OPEN
// un conteneur, le serveur alloue des entityId éphémères pour cette session
// afin que le client puisse référencer les items dans les requêtes de move.

/// <summary>Une entrée d'item dans la grille du conteneur (snapshot d'ouverture).</summary>
public struct S2C_ContainerItem
{
    public int EntityId;     // id éphémère alloué pour la session
    public int ConfigId;     // ItemConfig.ID
    public int SlotIndex;    // position dans la grille
}

/// <summary>Client → server : le joueur ouvre un conteneur (prop avec PropsConfig.Container.IsContainer).</summary>
public struct C2S_OpenContainer : NetworkMessage
{
    public int PropId;
}

/// <summary>Server → opener : snapshot du conteneur ouvert.</summary>
public struct S2C_ContainerOpened : NetworkMessage
{
    public int    PropId;
    public string PlaceId;
    public int    SlotCount;
    public byte[] AcceptedTypes;     // valeurs de l'enum ItemType ; vide = tous types
    public S2C_ContainerItem[] Items;
}

/// <summary>Server → opener : refus d'ouverture (range, permission, prop introuvable…).</summary>
public struct S2C_ContainerOpenFailed : NetworkMessage
{
    public int    PropId;
    public string ErrorMessage;
}

/// <summary>
/// Server → tous les clients de la room : un conteneur passe en état ouvert/fermé.
/// Strictement visuel (ouverture de porte) — la session DB est portée par les autres
/// messages container. Émis quand un joueur réussit l'ouverture et quand la session
/// se ferme (clic « X », joueur quitte la room, déconnexion).
/// </summary>
public struct S2C_ContainerVisualState : NetworkMessage
{
    public string RoomId;
    public int    PropId;
    public bool   IsOpen;
}

/// <summary>Client → server : ferme la session conteneur en cours.</summary>
public struct C2S_CloseContainer : NetworkMessage { }

/// <summary>
/// Client → server : déplace un item d'une place à une autre.
/// Place IDs canoniques :
///   - main : "hand_left:{charId}" / "hand_right:{charId}"
///   - poche : "pocket:{charId}"
///   - conteneur : retourné par le S2C_ContainerOpened précédent
/// </summary>
public struct C2S_MoveItem : NetworkMessage
{
    public int    EntityId;
    public string FromPlaceId;
    public string ToPlaceId;
    public int    ToSlotIndex;       // ignoré pour les places sans grille (mains)
}

/// <summary>Server → requesting client : résultat de la requête de move.</summary>
public struct S2C_MoveItemResult : NetworkMessage
{
    public bool   Success;
    public int    EntityId;
    public string ErrorMessage;
}

/// <summary>
/// Client → server : échange atomique de deux items entre deux places (hand↔container,
/// container↔container même placeId). Le hand↔hand passe par C2S_RequestSwapHands.
/// Chaque slot doit être référencé par son place (UUID pour conteneur, "hand_*:charId"
/// pour mains) et son slotIndex (ignoré pour les mains, mono-slot).
/// </summary>
public struct C2S_SwapItems : NetworkMessage
{
    public int    EntityIdA;
    public string PlaceIdA;
    public int    SlotIndexA;
    public int    EntityIdB;
    public string PlaceIdB;
    public int    SlotIndexB;
}
