using Mirror;
using Sim.Entities;
using UnityEngine;

// ═════════════════════════════════════════════════════════════════════════════
//  Server → Client
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Annonce le début d'un snapshot complet d'une room.
/// Suivi de PropCount × S2C_PropSpawn.
/// </summary>
public struct S2C_RoomSnapshot : NetworkMessage {
    public string RoomId;
    public int    PropCount;
}

/// <summary>Ordonne au client d'instancier un prop.</summary>
public struct S2C_PropSpawn : NetworkMessage {
    public int        PropId;
    public string     PrefabId;
    public string     RoomId;
    public Vector3    Position;
    public Quaternion Rotation;
    public PropType   Type;
    public byte[]     Payload;   // état initial (sérialisé selon PropType)
}

/// <summary>Ordonne au client d'appliquer un nouvel état à un prop existant.</summary>
public struct S2C_PropUpdate : NetworkMessage {
    public int      PropId;
    public string   RoomId;
    public PropType Type;
    public byte[]   Payload;
}

/// <summary>Ordonne au client de détruire un prop.</summary>
public struct S2C_PropRemove : NetworkMessage {
    public int    PropId;
    public string RoomId;
}

/// <summary>
/// Réponse ciblée (équivalent TargetRpc) après l'ouverture d'une DeliveryBox.
/// Le serveur a récupéré les livraisons via REST avant d'envoyer ce message.
/// </summary>
public struct S2C_DeliveryBoxOpened : NetworkMessage {
    public int        PropId;
    public string     RoomId;
    public Delivery[] Deliveries;  // Delivery est [Serializable], Mirror peut sérialiser les tableaux de classes simples
}

/// <summary>
/// Réponse ciblée après tentative d'achat dans un Dispenser.
/// Le serveur a validé le solde et spawné l'item si succès.
/// </summary>
public struct S2C_DispenserPurchaseResult : NetworkMessage {
    public int  PropId;
    public bool Success;
    public int  ItemId;   // -1 si échec
}

// ═════════════════════════════════════════════════════════════════════════════
//  Client → Server
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Demande d'interaction générique.
/// Le serveur route selon PropType et valide l'autorité.
/// </summary>
public struct C2S_PropInteraction : NetworkMessage {
    public int      PropId;
    public string   RoomId;
    public PropType Type;
    public byte[]   Payload;   // contexte selon PropType (SeatInteraction, DispenserInteraction…)
}

/// <summary>Le client entre dans une room et demande son snapshot.</summary>
public struct C2S_EnterRoom : NetworkMessage {
    public string RoomId;
}

/// <summary>Le client quitte une room.</summary>
public struct C2S_LeaveRoom : NetworkMessage {
    public string RoomId;
}
