using Mirror;
using Sim.Jobs;

/// <summary>
/// Server → Client. État complet du board pour une catégorie. Envoyé à
/// l'ouverture et rebroadcasté à chaque changement (ajout/prise/avancée/clôture).
/// Stratégie "snapshot complet" — la taille reste raisonnable pour un POC,
/// on pourra basculer en deltas plus tard si besoin.
/// </summary>
public struct JobBoardSnapshotMessage : NetworkMessage {
    public byte categoryByte;
    public JobBoardEntry[] entries;

    public JobCategory Category => (JobCategory)categoryByte;
}
