using System;

namespace Sim.Jobs {
    /// <summary>
    /// Types d'objets monde qu'un step de mission peut demander de mettre en
    /// évidence. Flags : un step peut en cibler plusieurs (ex. tri = colis + bacs).
    ///
    /// Ajouter un type pour un nouveau métier = nouvelle valeur ici (puissance de
    /// deux), un override <c>HighlightKind</c> sur le prop, et le(s) step(s) qui
    /// retournent ce kind via <c>GetHighlightKinds()</c>.
    /// </summary>
    [Flags]
    public enum MissionHighlightKind {
        None             = 0,
        Colis            = 1,
        PackagingMachine = 2,
        SortingBin       = 4,
    }
}
