using System.Collections.Generic;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Commande générée à la volée par PackageOrderGenerator. Plain C# — pas
    /// une SO. Contient les items à emballer (required), les leurres (decoys)
    /// et la seed utilisée pour la génération (rejouée côté serveur pour la
    /// validation anti-triche). Remplace PackageOrderDefinition.
    /// </summary>
    public class PackageOrder {
        public string orderId;
        public string customerName;
        public int seed;

        /// <summary>Items que le joueur doit emballer.</summary>
        public List<PackageItemDefinition> requiredItems;

        /// <summary>Leurres ajoutés au tray — placer un decoy pénalise le score.</summary>
        public List<PackageItemDefinition> decoys;
    }
}
