using Mirror;

/// <summary>
/// Client → Server. Le joueur a cliqué USE sur un bac de tri en tenant un colis.
/// Le serveur route vers le SortItemsStep actif du joueur et lui demande de
/// résoudre l'item tenu contre le bac désigné.
/// </summary>
public struct MissionSortDepositMessage : NetworkMessage {
    public string binId;
}
