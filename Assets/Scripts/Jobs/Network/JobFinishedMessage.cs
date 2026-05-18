using Mirror;
using Sim.Jobs;

/// <summary>Server → Client. La mission a atteint un état terminal (succès ou échec).</summary>
public struct JobFinishedMessage : NetworkMessage {
    public string instanceId;
    public JobStatus terminalStatus;
    public JobFailureReason failureReason;
    /// <summary>JobRating byte, valorisé uniquement quand terminalStatus == Completed.</summary>
    public byte rating;
    /// <summary>Durée totale de la mission en secondes (pour l'affichage côté client).</summary>
    public float elapsedSeconds;
    /// <summary>Nombre de colis bien triés (SortItems step). 0 si non applicable.</summary>
    public int correctCount;
    /// <summary>Nombre total de colis à trier (SortItems step). 0 si non applicable.</summary>
    public int totalCount;
    /// <summary>JobResultVariant byte. Sélectionne la vue à afficher côté client.</summary>
    public byte resultVariant;
    /// <summary>Argent gagné par le joueur pour cette mission (somme des MoneyReward / ScoreModulatedMoneyReward).</summary>
    public int moneyEarned;
}
