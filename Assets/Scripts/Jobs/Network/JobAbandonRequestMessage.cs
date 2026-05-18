using Mirror;

/// <summary>Client → Server. Le joueur abandonne une mission active.</summary>
public struct JobAbandonRequestMessage : NetworkMessage {
    public string instanceId;
}
