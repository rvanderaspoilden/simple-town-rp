using Mirror;
using Sim.Missions;

/// <summary>Server → Client. La mission a atteint un état terminal (succès ou échec).</summary>
public struct MissionFinishedMessage : NetworkMessage {
    public string instanceId;
    public MissionStatus terminalStatus;
    public MissionFailureReason failureReason;
    /// <summary>MissionRating byte, valorisé uniquement quand terminalStatus == Completed.</summary>
    public byte rating;
    /// <summary>Durée totale de la mission en secondes (pour l'affichage côté client).</summary>
    public float elapsedSeconds;
    /// <summary>Nombre de colis bien triés (SortItems step). 0 si non applicable.</summary>
    public int correctCount;
    /// <summary>Nombre total de colis à trier (SortItems step). 0 si non applicable.</summary>
    public int totalCount;
    /// <summary>Argent gagné par le joueur pour cette mission (somme des MoneyReward / ScoreModulatedMoneyReward).</summary>
    public int moneyEarned;
    /// <summary>Étiquettes utilisateur des gains constellation (ex "Ingénieux", "Livreur").
    /// Parallel array : <see cref="constellationGainLabels"/>[i] correspond à
    /// <see cref="constellationGainAmounts"/>[i]. Inclut les branches et les professions
    /// (déjà résolues en libellé côté serveur via ConstellationGraphConfig).</summary>
    public string[] constellationGainLabels;
    public int[]    constellationGainAmounts;
}
