using System.Collections.Generic;
using UnityEngine;

namespace Sim.Scriptables {
    /// <summary>
    /// Catalogue d'une boutique de vêtements : les articles proposés en cabine d'essayage
    /// (<see cref="ClothingRackBehaviour"/> → FittingRoomUI). Réutilise <see cref="ItemPrice"/>
    /// (pattern MerchantNpcConfig) ; les entrées couvrent les DEUX genres (même label), l'UI
    /// filtre sur le genre du personnage local et le serveur revalide. Asset par défaut généré
    /// par Tools ▸ The BROZ ▸ APS2 ▸ Generate Clothing Shop Catalog.
    /// </summary>
    [CreateAssetMenu(menuName = "Configurations/Clothing Shop Catalog", fileName = "New Clothing Shop Catalog")]
    public class ClothingShopCatalogConfig : ScriptableObject {
        public const string DefaultResourcePath = "Configurations/Shops/ClothingShopCatalog";

        [Tooltip("Libellé affiché en en-tête de la cabine d'essayage.")]
        [SerializeField] private string shopLabel = "Boutique de vêtements";

        [Tooltip("Articles en vente (vêtements uniquement — ClothingConfig). Les deux genres.")]
        [SerializeField] private List<ItemPrice> entries = new List<ItemPrice>();

        public string ShopLabel => shopLabel;

        public IReadOnlyList<ItemPrice> Entries => entries;

        /// <summary>Validation serveur : l'article est-il au catalogue ? Renvoie prix + config.</summary>
        public bool TryGetPrice(int itemConfigId, out int price, out ItemConfig cfg) {
            foreach (ItemPrice entry in entries) {
                if (entry.item != null && entry.item.ID == itemConfigId) {
                    price = entry.price;
                    cfg = entry.item;
                    return true;
                }
            }
            price = 0;
            cfg = null;
            return false;
        }

        public static ClothingShopCatalogConfig LoadDefault() =>
            Resources.Load<ClothingShopCatalogConfig>(DefaultResourcePath);
    }
}
