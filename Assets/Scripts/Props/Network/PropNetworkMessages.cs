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
    public int        PrefabId;   // PropsConfig.GetId()
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

/// <summary>
/// Met à jour la position/rotation d'un prop (édition apartment).
/// Séparé de PropUpdate parce que la transform n'est pas dans le payload.
/// </summary>
public struct S2C_PropTransform : NetworkMessage {
    public int        PropId;
    public string     RoomId;
    public Vector3    Position;
    public Quaternion Rotation;
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
    public Delivery[] Deliveries;
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

/// <summary>
/// Confirme côté local-player la fin d'un build/edit (sortie du build mode).
/// </summary>
public struct S2C_BuildAck : NetworkMessage {
    public bool Success;
}

/// <summary>
/// Diffuse à tous les clients de la room le son de sonnette d'une porte.
/// Le client joue le son sur le DoorBehaviour identifié par PropId.
/// </summary>
public struct S2C_DoorRing : NetworkMessage {
    public int    PropId;
    public string RoomId;
}

// ═════════════════════════════════════════════════════════════════════════════
//  Client → Server
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Demande d'interaction générique sur un prop existant.
/// Le serveur route selon PropType et valide l'autorité.
/// </summary>
public struct C2S_PropInteraction : NetworkMessage {
    public int      PropId;
    public string   RoomId;
    public PropType Type;
    public byte[]   Payload;
}

/// <summary>Le client entre dans une room et demande son snapshot.</summary>
public struct C2S_EnterRoom : NetworkMessage {
    public string RoomId;
}

/// <summary>Le client quitte une room.</summary>
public struct C2S_LeaveRoom : NetworkMessage {
    public string RoomId;
}

/// <summary>
/// Demande de création d'un prop (via build mode après livraison).
/// Le serveur valide la livraison via REST, supprime la livraison, spawn le prop,
/// rafraîchit le compteur de la DeliveryBox, et renvoie un S2C_BuildAck au client.
/// </summary>
public struct C2S_BuildProp : NetworkMessage {
    public string     RoomId;
    public int        DeliveryBoxPropId;   // pour rafraîchir le compteur après spawn
    public string     DeliveryId;          // _id mongodb de la livraison à supprimer
    public int        PropConfigId;
    public int        PresetId;
    public Vector3    Position;
    public Quaternion Rotation;

    // Initial state pour PaintBucket (Type == DeliveryType.COVER)
    public int        PaintConfigId;       // -1 si non-bucket
    public float      ColorR, ColorG, ColorB;
}

/// <summary>Demande de déplacement d'un prop existant (édition).</summary>
public struct C2S_EditProp : NetworkMessage {
    public string     RoomId;
    public int        PropId;
    public Vector3    Position;
    public Quaternion Rotation;
}

/// <summary>Demande de suppression d'un prop (vente).</summary>
public struct C2S_RemoveProp : NetworkMessage {
    public string RoomId;
    public int    PropId;
}

/// <summary>
/// Le client demande au serveur d'utiliser l'ascenseur dans sa room courante.
/// Pas de PropId — le serveur route via PlayerRoomTracker → TeleporterBehaviour.TryGetByRoom.
/// </summary>
public struct C2S_TeleporterUse : NetworkMessage {
    public int FloorDestination;
}

/// <summary>
/// Payload arbitraire attaché à une room (au-delà des props individuels).
/// Broadcast à l'entrée de room et sur chaque changement via ServerPropManager.SetRoomState.
/// </summary>
public struct S2C_RoomState : NetworkMessage {
    public string RoomId;
    public byte[] Payload;
}

/// <summary>
/// Le client envoie de nouveaux paramètres de revêtement pour les murs.
/// CoversJson = CoverDataWrapper.Serialize() — tableau de Sim.CoverData JSON-encodé.
/// </summary>
public struct C2S_ApplyWallCovers : NetworkMessage {
    public string RoomId;
    public byte[] CoversJson;
}

/// <summary>
/// Le client envoie de nouveaux paramètres de revêtement pour les sols.
/// CoversJson = CoverDataWrapper.Serialize() — tableau de Sim.CoverData JSON-encodé.
/// </summary>
public struct C2S_ApplyGroundCovers : NetworkMessage {
    public string RoomId;
    public byte[] CoversJson;
}
