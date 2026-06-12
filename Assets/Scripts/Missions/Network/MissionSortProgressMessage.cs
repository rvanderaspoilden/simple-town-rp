using Mirror;

/// <summary>
/// Server → Owner. Push après chaque dépôt sur un bac, et une dernière fois
/// quand le step est terminé (Finished=true). Le HUD affiche
/// "ResolvedCount / TotalCount" tant que le step est actif ; à la complétion,
/// le client ouvre un panneau de résultat avec correctCount / totalCount /
/// AccuracyRatio.
/// </summary>
public struct MissionSortProgressMessage : NetworkMessage {
    public string instanceId;
    public int    resolvedCount;
    public int    correctCount;
    public int    totalCount;
    public bool   finished;
    public float  accuracyRatio;
    public byte   rating; // MissionRating (0=Poor … 3=Perfect), valid only when finished
}
