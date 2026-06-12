using Mirror;

/// <summary>
/// Server → Client. État complet du board pour un métier (ProfessionConfig.id).
/// Envoyé à l'ouverture et rebroadcasté à chaque changement.
/// </summary>
public struct MissionBoardSnapshotMessage : NetworkMessage {
    public string professionId;
    public MissionBoardEntry[] entries;
}
