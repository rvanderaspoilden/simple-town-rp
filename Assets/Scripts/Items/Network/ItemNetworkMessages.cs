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

/// <summary>Server → clients in room: niveau de carburant d'un item (bidon) mis à jour.</summary>
public struct S2C_ItemFuel : NetworkMessage
{
    public int   EntityId;
    public float Fuel;
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
    public int PropConfigId; // >0 = meuble emballé (icône PropsConfig.Sprite, non-draggable) ; 0 = item normal
    public int PropPresetId; // variant du meuble emballé (pour la preview au déballage)
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

/// <summary>Client → server : ferme une session conteneur. <see cref="ItemContainer"/>
/// distingue le colis (item-conteneur) d'un meuble (prop) — les deux peuvent être
/// ouverts en même temps, chaque panneau ferme la sienne.</summary>
public struct C2S_CloseContainer : NetworkMessage { public bool ItemContainer; }

// ── Item-as-container (« package ») ───────────────────────────────────────────
// Un item tenu en main peut être lui-même un conteneur (ItemConfig.Container).
// Place backend "item_container:{itemUuid}" (owner_id = uuid de l'item package).
// Même machinerie que les conteneurs de prop (sessions, snapshot, move/swap) ;
// seul l'identifiant d'ouverture change (EntityId de l'item au lieu d'un PropId).

/// <summary>Client → server : le joueur ouvre un item-conteneur qu'il tient en main.</summary>
public struct C2S_OpenItemContainer : NetworkMessage
{
    public int EntityId;   // entityId de l'item package en main
}

/// <summary>Server → opener : snapshot de l'item-conteneur ouvert.</summary>
public struct S2C_ItemContainerOpened : NetworkMessage
{
    public int    EntityId;          // entityId du package (matching ouverture optimiste)
    public string PlaceId;           // UUID backend de la place item_container
    public int    SlotCount;
    public byte[] AcceptedTypes;     // valeurs ItemType ; vide = tous types
    public S2C_ContainerItem[] Items;
}

/// <summary>Server → opener : refus d'ouverture d'un item-conteneur.</summary>
public struct S2C_ItemContainerOpenFailed : NetworkMessage
{
    public int    EntityId;
    public string ErrorMessage;
}

/// <summary>
/// Client → server : « emballer » un prop possédé dans le colis tenu. Le serveur
/// déplace la ligne `props` (UUID conservé) dans la place du colis (is_built=false).
/// </summary>
public struct C2S_PackProp : NetworkMessage
{
    public int PropId;
}

/// <summary>
/// Client → server : « déballer » un meuble emballé du colis → re-pose à-construire
/// dans l'appartement (build mode). Le serveur résout le meuble via la place du colis
/// (bridge de <see cref="PackageEntityId"/>) + <see cref="SlotIndex"/>, PATCH la ligne
/// `props` vers la place de l'appartement + position (UUID conservé), re-spawn runtime.
/// </summary>
public struct C2S_UnpackProp : NetworkMessage
{
    public int PackageEntityId;   // entityId runtime du colis (tenu OU au sol)
    public int SlotIndex;         // slot du meuble emballé dans la grille
    public Vector3 Position;
    public Quaternion Rotation;
}

/// <summary>
/// Client → server : lâcher au sol un item rangé dans un colis tenu OU dans une poche.
/// <see cref="EntityId"/> = id de session de l'item (résolu côté serveur dans la session
/// colis ouverte ou la session poche). L'item devient un item-monde au pied du joueur.
/// </summary>
public struct C2S_DropFromInventory : NetworkMessage
{
    public int EntityId;
}

// ── Poches (toujours ouvertes pour le joueur local) ───────────────────────────
// Modèle identique à un conteneur 2 slots, persisté en DB à la place
// "pocket:{charId}". Une PocketSession serveur est créée à la connexion (après
// EnsurePlaces) et tient les entityId éphémères des items en poche le temps
// de la session réseau.

/// <summary>Une entrée d'item dans la poche (slot 0 = gauche, slot 1 = droite).</summary>
public struct S2C_PocketItem
{
    public int EntityId;
    public int ConfigId;
    public int SlotIndex;
}

/// <summary>
/// Server → opener : snapshot complet du contenu des poches. Émis à la
/// connexion puis après chaque move qui touche la poche.
/// </summary>
public struct S2C_PocketSync : NetworkMessage
{
    public string PlaceId;          // UUID backend de la place pocket
    public int    SlotCount;        // capacité (2 en MVP)
    public S2C_PocketItem[] Items;
}

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
