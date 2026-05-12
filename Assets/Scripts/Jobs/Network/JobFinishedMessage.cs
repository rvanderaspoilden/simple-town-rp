using Mirror;
using Sim.Jobs;

/// <summary>Server → Client. La mission a atteint un état terminal (succès ou échec).</summary>
public struct JobFinishedMessage : NetworkMessage {
    public string instanceId;
    public JobStatus terminalStatus;
    public JobFailureReason failureReason;
}
