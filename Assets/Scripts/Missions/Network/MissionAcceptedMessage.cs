using Mirror;

/// <summary>Client → Server. Le joueur accepte une mission qui lui a été proposée.</summary>
public struct MissionAcceptedMessage : NetworkMessage {
    public string instanceId;
}
