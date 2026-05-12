using Sim.Jobs;

/// <summary>
/// Snapshot d'une mission tel que vu sur le board. Sérialisable par
/// Mirror via Writer/Reader auto-générés. N'implémente pas NetworkMessage —
/// c'est un payload utilisé dans JobBoardSnapshotMessage.
/// </summary>
public struct JobBoardEntry {
    public string instanceId;
    public string jobId;
    public byte statusByte;
    public int currentStepIndex;
    public uint ownerNetId;
    public string ownerName;

    public JobStatus Status => (JobStatus)statusByte;
}
