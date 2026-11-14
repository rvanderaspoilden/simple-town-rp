using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Vignette d'un article AU PANIER dans le bandeau « Panier » de la cabine d'essayage
    /// (<see cref="FittingRoomUI"/>) : icône + prix + croix de retrait. La croix retire l'article
    /// du panier (et de la preview) sans changer d'onglet. Template désactivé autoré dans le
    /// prefab HUD (Tools ▸ The BROZ ▸ Build Fitting Room UI), cloné par article du panier.
    /// </summary>
    public class FittingRoomCartItem : MonoBehaviour {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private Button removeButton;

        public ItemConfig Item { get; private set; }

        public void Bind(ItemConfig item, int price, System.Action<FittingRoomCartItem> onRemove) {
            Item = item;

            if (icon != null) {
                icon.sprite = item.Icon;
                icon.enabled = item.Icon != null;
            }
            if (priceLabel != null) priceLabel.text = $"{price} $";
            if (removeButton != null) {
                removeButton.onClick.RemoveAllListeners();
                removeButton.onClick.AddListener(() => onRemove?.Invoke(this));
            }
        }

        /// <summary>Retrait inhibé pendant l'achat en cours.</summary>
        public void SetRemovable(bool removable) {
            if (removeButton != null) removeButton.interactable = removable;
        }
    }
}
