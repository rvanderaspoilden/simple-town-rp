using Mirror;
using Sim.SubGames.Packaging;

/// <summary>
/// Client → Server. Le joueur a interagi avec une machine et veut faire
/// avancer son step UseMachine actif. Le serveur route vers le step enregistré
/// pour ce joueur. `machineId` est informationnel pour l'instant (logs / futurs
/// matchings catégorie ↔ machine), pas validé.
///
/// Si le step a un mini-jeu d'emballage attaché, `snapshot` contient les
/// placements du joueur — le serveur régénère l'ordre depuis la seed du
/// snapshot + son catalog autoritaire, puis recalcule le score (anti-triche).
/// Si pas de mini-jeu, le snapshot est laissé vide.
/// </summary>
public struct JobUseMachineMessage : NetworkMessage {
    public string machineId;
    public PackagePlacementSnapshot snapshot;
}
