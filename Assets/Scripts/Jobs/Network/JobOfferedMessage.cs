using Mirror;
using Sim.Jobs;

public struct JobOfferedMessage : NetworkMessage {
    public string instanceId;
    public string jobId;

    public JobTargetKind primaryTargetKind;
    public string primaryTargetId;
    public JobTargetKind secondaryTargetKind;
    public string secondaryTargetId;

    public string payloadItemId;
}
