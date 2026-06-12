using Mirror;

/// <summary>Client → Server. Le joueur ferme un board (désabonnement).</summary>
public struct MissionBoardCloseMessage : NetworkMessage {
    public string professionId;
}
