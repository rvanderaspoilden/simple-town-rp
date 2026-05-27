using Mirror;

/// <summary>Server → Client. La mission a progressé d'un step.</summary>
public struct JobStepAdvancedMessage : NetworkMessage {
    public string instanceId;
    public int newStepIndex;
    public string promptKey;
    public string currentTargetId;
    public string currentTargetName;

    /// <summary>Le beacon monde du JobPoint doit-il s'allumer pour ce step
    /// (steps de navigation Reach/Deliver uniquement).</summary>
    public bool showTargetBeacon;
}
