using Mirror;

/// <summary>
/// Messages du système véhicule (concessionnaire NPC + garage). Émis par des objets sans
/// NetworkIdentity (vue NPC client, GarageDoor) qui ne peuvent donc pas utiliser de [Command].
/// Gérés côté serveur par <see cref="Sim.VehicleSystemBootstrap"/>.
///
/// Le catalogue n'est PAS transmis au spawn du NPC (snapshots légers) : il est demandé à
/// l'ouverture de la boutique (C2S_RequestVehicleCatalog → S2C_VehicleCatalog), puis chaque
/// achat est un aller-retour (C2S_BuyVehicle → S2C_VehicleBuyResult) — même découpage que le
/// pipeline marchand (NpcMerchantMessages).
///
/// Namespace global (convention Mirror du projet pour les messages réseau).
/// </summary>
public struct C2S_RequestVehicleCatalog : NetworkMessage {
    public int npcId;
}

/// <summary>Une ligne du catalogue : id de VehicleConfig + prix + libellé d'affichage.</summary>
public struct VehicleCatalogEntry {
    public string configId;
    public int    price;
    public string label;
}

/// <summary>Réponse serveur : le catalogue complet du vendeur de véhicules demandé.</summary>
public struct S2C_VehicleCatalog : NetworkMessage {
    public int                   npcId;
    public string                vendorLabel;
    public VehicleCatalogEntry[] entries;
}

public struct C2S_BuyVehicle : NetworkMessage {
    public int    npcId;    // NPC vendeur auprès duquel l'achat est fait (validation catalogue + room)
    public string configId; // id de VehicleConfig (prix + modèle + prefab résolus serveur via DatabaseManager)
}

/// <summary>
/// Résultat d'un achat de véhicule. <see cref="reasonCode"/> (aligné sur S2C_MerchantBuyResult) :
///   0 = succès,
///   1 = véhicule indisponible / hors catalogue,
///   2 = fonds insuffisants,
///   4 = vendeur absent / pas un vendeur / hors-room.
/// </summary>
public struct S2C_VehicleBuyResult : NetworkMessage {
    public int    npcId;
    public string configId;
    public bool   success;
    public byte   reasonCode;
}

public struct C2S_TakeOutVehicle : NetworkMessage {
    public string vehicleId;
    public string doorKey;
}

public struct C2S_StoreVehicle : NetworkMessage {
    public string doorKey;
}
