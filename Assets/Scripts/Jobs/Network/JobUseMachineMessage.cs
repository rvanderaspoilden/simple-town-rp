using Mirror;

/// <summary>
/// Client → Server. Le joueur a interagi avec une machine et veut faire
/// avancer son step UseMachine actif. Le serveur route vers le step enregistré
/// pour ce joueur. `machineId` est informationnel pour l'instant (logs / futurs
/// matchings catégorie ↔ machine), pas validé.
/// </summary>
public struct JobUseMachineMessage : NetworkMessage {
    public string machineId;
}
