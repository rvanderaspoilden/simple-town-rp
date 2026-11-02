using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Une ligne de la boutique véhicules (<see cref="VehicleShopUI"/>) : libellé + prix + bouton
    /// Acheter. Le GameObject porteur est un TEMPLATE désactivé autoré dans le prefab HUD ; la
    /// modale le clone une fois par véhicule du catalogue (pattern <see cref="MerchantShopRow"/>).
    /// </summary>
    public class VehicleShopRow : MonoBehaviour {
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private Button   buyButton;

        public string ConfigId { get; private set; }
        public int    Price    { get; private set; }

        private System.Action<string> _onBuy;

        public void Bind(VehicleCatalogEntry entry, System.Action<string> onBuy) {
            ConfigId = entry.configId;
            Price    = entry.price;
            _onBuy   = onBuy;

            if (label      != null) label.text      = entry.label;
            if (priceLabel != null) priceLabel.text = $"{entry.price} BC";

            if (buyButton != null) {
                buyButton.onClick.RemoveAllListeners();
                buyButton.onClick.AddListener(() => _onBuy?.Invoke(ConfigId));
            }
        }

        /// <summary>Active/grise le bouton selon que le joueur peut s'offrir le véhicule.</summary>
        public void SetAffordable(bool canAfford) {
            if (buyButton != null) buyButton.interactable = canAfford;
        }
    }
}
