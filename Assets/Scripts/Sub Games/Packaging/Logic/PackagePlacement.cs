using System;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Placement d'un item dans un colis, sérialisable par Mirror.
    /// instanceIndex = index dans PackageOrderDefinition.items côté serveur,
    /// jamais une référence d'asset → le serveur ne peut être trompé sur
    /// l'identité de l'item.
    /// </summary>
    [Serializable]
    public struct PackagePlacement {
        public byte instanceIndex;
        public byte originX;
        public byte originY;
        public byte rotation;
    }

    /// <summary>
    /// Snapshot envoyé au serveur pour validation anti-triche. Le serveur
    /// utilise son propre PackageOrderDefinition (via PackagagingSubGameConfig
    /// attaché au step) et ses propres poids — le client ne peut influer que
    /// sur les placements.
    /// </summary>
    [Serializable]
    public struct PackagePlacementSnapshot {
        public string orderId;
        public byte gridWidth;
        public byte gridHeight;
        public PackagePlacement[] placements;
    }
}
