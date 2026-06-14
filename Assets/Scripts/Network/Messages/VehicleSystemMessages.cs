using Mirror;

/// <summary>
/// Messages du système véhicule (concession + garage). Émis par les objets de scène NON-réseau
/// (VehicleShop, GarageDoor) qui n'ont pas de NetworkIdentity et ne peuvent donc pas utiliser
/// de [Command]. Gérés côté serveur par <see cref="Sim.VehicleSystemBootstrap"/>.
///
/// Namespace global (convention Mirror du projet pour les messages réseau).
/// </summary>
public struct C2S_BuyVehicle : NetworkMessage {
    public string configId; // id de VehicleConfig (prix + modèle + prefab résolus serveur via DatabaseManager)
}

public struct C2S_TakeOutVehicle : NetworkMessage {
    public string vehicleId;
    public string doorKey;
}

public struct C2S_StoreVehicle : NetworkMessage {
    public string doorKey;
}
