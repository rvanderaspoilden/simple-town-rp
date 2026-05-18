namespace Sim.Jobs {
    /// <summary>
    /// Identifie le type d'affichage à montrer dans JobCompletionResultPanel
    /// à la fin d'une mission. Exposé par chaque JobScoringDefinition et
    /// transmis au client via JobFinishedMessage.
    /// </summary>
    public enum JobResultVariant : byte {
        Default  = 0,
        Time     = 1,
        Sort     = 2,
        MiniGame = 3,
    }
}
