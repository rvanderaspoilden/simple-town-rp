using Mirror;

/// <summary>Client → Server. Le joueur prend une mission disponible depuis le board.</summary>
public struct JobBoardTakeMessage : NetworkMessage {
    public string instanceId;
}
