using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Une commande client : nom + liste d'items à emballer.
    /// </summary>
    [CreateAssetMenu(fileName = "PackageOrderDefinition", menuName = "Configurations/Packaging/Order")]
    public class PackageOrderDefinition : ScriptableObject {
        [Tooltip("Identifiant stable utilisé pour la validation serveur. Auto-rempli avec le nom de l'asset si vide.")]
        public string orderId;
        public string customerName;
        public PackageItemDefinition[] items;

#if UNITY_EDITOR
        private void OnValidate() {
            if (string.IsNullOrEmpty(orderId)) orderId = name;
        }
#endif
    }
}
