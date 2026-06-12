using Mirror;

/// <summary>Client → Server. Le joueur abandonne une mission active.</summary>
public struct MissionAbandonRequestMessage : NetworkMessage {
    public string instanceId;
}
