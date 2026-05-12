using Mirror;

/// <summary>Server → Client. La mission a progressé d'un step.</summary>
public struct JobStepAdvancedMessage : NetworkMessage {
    public string instanceId;
    public int newStepIndex;
    public string promptKey;
}
