using System;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Placement d'un item dans un colis, sérialisable par Mirror.
    /// instanceIndex = index dans PackageOrder (required puis decoys) côté serveur,
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
    /// rejoue PackageOrderGenerator avec la même seed et le catalog autoritaire
    /// (via la PackagingSubGameConfig attachée au step) pour reconstruire
    /// l'ordre, puis recalcule le score à partir des placements du client.
    /// </summary>
    [Serializable]
    public struct PackagePlacementSnapshot {
        public string orderId;
        public int seed;
        public byte gridWidth;
        public byte gridHeight;
        public PackagePlacement[] placements;
    }
}
