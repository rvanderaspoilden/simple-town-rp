using Sim.Missions;

/// <summary>
/// Snapshot d'une mission tel que vu sur le board. Sérialisable par
/// Mirror via Writer/Reader auto-générés. N'implémente pas NetworkMessage —
/// c'est un payload utilisé dans MissionBoardSnapshotMessage.
/// </summary>
public struct MissionBoardEntry {
    public string instanceId;
    public string missionId;
    public byte statusByte;
    public int currentStepIndex;
    public uint ownerNetId;
    public string ownerName;

    public MissionStatus Status => (MissionStatus)statusByte;
}
