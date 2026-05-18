using Mirror;
using Sim.Jobs;

/// <summary>Client → Server. Le joueur ouvre un board d'une catégorie donnée.</summary>
public struct JobBoardOpenMessage : NetworkMessage {
    public byte categoryByte;
    public JobCategory Category => (JobCategory)categoryByte;
}
