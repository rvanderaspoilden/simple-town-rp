using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Une ligne de la boutique marchand (<see cref="MerchantShopUI"/>) : libellé + prix + bouton
    /// Acheter. Le GameObject porteur est un TEMPLATE désactivé autoré dans le prefab HUD ; la
    /// modale le clone une fois par item du catalogue (cf. mémoire « author UI in prefab »).
    /// </summary>
    public class MerchantShopRow : MonoBehaviour {
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private Button   buyButton;

        public int ItemConfigId { get; private set; }
        public int Price        { get; private set; }

        private System.Action<int> _onBuy;

        public void Bind(MerchantCatalogEntry entry, System.Action<int> onBuy) {
            ItemConfigId = entry.ItemConfigId;
            Price        = entry.Price;
            _onBuy       = onBuy;

            if (label      != null) label.text      = entry.Label;
            if (priceLabel != null) priceLabel.text = $"{entry.Price} BC";

            if (buyButton != null) {
                buyButton.onClick.RemoveAllListeners();
                buyButton.onClick.AddListener(() => _onBuy?.Invoke(ItemConfigId));
            }
        }

        /// <summary>Active/grise le bouton selon que le joueur peut s'offrir l'item.</summary>
        public void SetAffordable(bool canAfford) {
            if (buyButton != null) buyButton.interactable = canAfford;
        }
    }
}
