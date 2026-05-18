using Mirror;
using Sim.Jobs;

/// <summary>Client → Server. Le joueur ferme un board (désabonnement).</summary>
public struct JobBoardCloseMessage : NetworkMessage {
    public byte categoryByte;
    public JobCategory Category => (JobCategory)categoryByte;
}
