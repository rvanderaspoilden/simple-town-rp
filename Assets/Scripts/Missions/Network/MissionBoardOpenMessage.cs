using Mirror;

/// <summary>Client → Server. Le joueur ouvre le board d'un métier (ProfessionConfig.id).</summary>
public struct MissionBoardOpenMessage : NetworkMessage {
    public string professionId;
}
