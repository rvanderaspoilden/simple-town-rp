using Mirror;
using UnityEngine;

// ═════════════════════════════════════════════════════════════════════════════
//  Land parcel system messages. Emitted by non-networked scene objects (ForSaleSign)
//  and by the build pipeline when the current buildable space is a land parcel.
//  Handled server-side by Sim.LandSystemBootstrap.
//
//  Namespace global (convention Mirror du projet pour les messages réseau).
// ═════════════════════════════════════════════════════════════════════════════

// ── Client → Server ──────────────────────────────────────────────────────────

/// <summary>Le client confirme l'achat d'un lot. parcelId = LandConfig.id (résolu serveur
/// via DatabaseManager + LandParcelController.ById).</summary>
public struct C2S_BuyLand : NetworkMessage {
    public int parcelId;
}

/// <summary>
/// Création d'un prop sur un lot possédé (build mode depuis une livraison). Même flux que
/// <see cref="C2S_BuildProp"/> mais routé par parcelId (le terrain partage la room "city" donc
/// le serveur ne peut pas router par connexion comme pour les appartements).
/// </summary>
public struct C2S_BuildLandProp : NetworkMessage {
    public int        ParcelId;
    public int        DeliveryBoxPropId;
    public string     DeliveryId;
    public int        PropConfigId;
    public int        PresetId;
    public Vector3    Position;
    public Quaternion Rotation;
    public int        PaintConfigId;       // -1 si non-bucket
    public float      ColorR, ColorG, ColorB;
}

/// <summary>Déplacement d'un prop existant sur un lot (édition).</summary>
public struct C2S_EditLandProp : NetworkMessage {
    public int        ParcelId;
    public int        PropId;
    public Vector3    Position;
    public Quaternion Rotation;
}

/// <summary>Suppression d'un prop d'un lot.</summary>
public struct C2S_RemoveLandProp : NetworkMessage {
    public int ParcelId;
    public int PropId;
}

/// <summary>Le propriétaire revend son lot à l'office foncier (remboursement partiel).
/// parcelId = LandConfig.id.</summary>
public struct C2S_SellLand : NetworkMessage {
    public int parcelId;
}

/// <summary>Le client prend en location un lot disponible (loyer récurrent prélevé par
/// l'office foncier). parcelId = LandConfig.id.</summary>
public struct C2S_RentLand : NetworkMessage {
    public int parcelId;
}

/// <summary>Le locataire résilie son bail (rend le terrain, sans remboursement).
/// parcelId = LandConfig.id.</summary>
public struct C2S_TerminateLandLease : NetworkMessage {
    public int parcelId;
}

// ── Server → Client ──────────────────────────────────────────────────────────

/// <summary>Diffusé à tous les clients quand l'état d'occupation d'un lot change.
/// owner/tenant vides = lot disponible (à vendre ou à louer). Un lot a SOIT un
/// propriétaire (acheté) SOIT un locataire (loué), jamais les deux.</summary>
public struct S2C_LandOwnership : NetworkMessage {
    public int    parcelId;
    public string ownerId;
    public string ownerName;
    public string tenantId;
    public string tenantName;
}

/// <summary>Snapshot d'occupation envoyé à un client qui se connecte. Tableaux parallèles :
/// parcelIds[i] est possédé par ownerIds[i]/ownerNames[i] OU loué par tenantIds[i]/tenantNames[i].</summary>
public struct S2C_LandSnapshot : NetworkMessage {
    public int[]    parcelIds;
    public string[] ownerIds;
    public string[] ownerNames;
    public string[] tenantIds;
    public string[] tenantNames;
}
