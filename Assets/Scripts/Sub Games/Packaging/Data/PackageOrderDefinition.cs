using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Une commande client : nom + liste d'items à emballer.
    /// </summary>
    [CreateAssetMenu(fileName = "PackageOrderDefinition", menuName = "Configurations/Packaging/Order")]
    public class PackageOrderDefinition : ScriptableObject {
        public string customerName;
        public PackageItemDefinition[] items;
    }
}
