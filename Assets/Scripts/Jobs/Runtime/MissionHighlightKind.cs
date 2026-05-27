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

    /// <summary>
    /// Phase d'affichage d'un highlight selon l'état des mains du joueur local.
    /// Générique : tout métier réutilise ces phases (tri = colis/bacs ;
    /// nettoyage = déchets/poubelles ; etc.).
    ///  • <see cref="Always"/>   : visible dès que le kind est actif (défaut — ex. machine d'emballage).
    ///  • <see cref="HandsFree"/>: visible seulement mains libres (cible de RAMASSAGE).
    ///  • <see cref="Holding"/>  : visible seulement en tenant un colis (cible de DÉPÔT).
    /// </summary>
    public enum MissionHighlightPhase {
        Always,
        HandsFree,
        Holding,
    }
}
