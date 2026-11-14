using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Carte d'article de la grille de la cabine d'essayage (<see cref="FittingRoomUI"/>) :
    /// icône + prix, coche verte quand l'article est AU PANIER (porté en preview), badge bleu
    /// « déjà porté » quand le personnage le porte réellement, bordure claire quand il est
    /// SÉLECTIONNÉ (affiché dans le panneau de détail). L'article est un <see cref="ItemConfig"/>
    /// implémentant <see cref="IApsWearable"/> (vêtement ou sac à dos). Template désactivé autoré
    /// dans le prefab HUD (Tools ▸ The BROZ ▸ Build Fitting Room UI), cloné par article.
    /// </summary>
    public class FittingRoomCard : MonoBehaviour {
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private GameObject checkmark;
        [Tooltip("Badge « déjà porté » (coin haut-gauche) : le personnage porte actuellement cet article.")]
        [SerializeField] private GameObject wornBadge;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private Button button;

        private static readonly Color NormalColor   = new Color(0.14f, 0.15f, 0.18f, 1f);
        private static readonly Color SelectedColor = new Color(0.22f, 0.32f, 0.46f, 1f);

        public ItemConfig Item { get; private set; }
        public IApsWearable Wearable => Item as IApsWearable;
        public int Price { get; private set; }

        public void Bind(ItemConfig item, int price, System.Action<FittingRoomCard> onClicked) {
            Item = item;
            Price = price;

            if (icon != null) {
                icon.sprite = item.Icon;
                icon.enabled = item.Icon != null;
            }
            if (priceLabel != null) priceLabel.text = $"{price} $";
            if (button != null) {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onClicked?.Invoke(this));
            }
            SetInCart(false);
            SetSelected(false);
            SetWorn(false);
        }

        /// <summary>Coche « au panier » (l'article est porté en preview).</summary>
        public void SetInCart(bool inCart) {
            if (checkmark != null) checkmark.SetActive(inCart);
        }

        /// <summary>Badge « déjà porté » : l'article est celui que le personnage porte réellement
        /// (garde-robe répliquée / sac au dos), à distinguer du panier/preview.</summary>
        public void SetWorn(bool worn) {
            if (wornBadge != null) wornBadge.SetActive(worn);
        }

        /// <summary>Surbrillance « sélectionné » (affiché dans le panneau de détail).</summary>
        public void SetSelected(bool selected) {
            if (background != null) background.color = selected ? SelectedColor : NormalColor;
        }
    }
}
