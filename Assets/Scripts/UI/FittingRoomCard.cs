using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Carte d'article de la grille de la cabine d'essayage (<see cref="FittingRoomUI"/>) :
    /// icône + prix, coche verte quand l'article est AU PANIER (porté en preview), bordure
    /// claire quand il est SÉLECTIONNÉ (affiché dans le panneau de détail). Template désactivé
    /// autoré dans le prefab HUD (Tools ▸ The BROZ ▸ Build Fitting Room UI), cloné par article.
    /// </summary>
    public class FittingRoomCard : MonoBehaviour {
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private GameObject checkmark;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private Button button;

        private static readonly Color NormalColor   = new Color(0.14f, 0.15f, 0.18f, 1f);
        private static readonly Color SelectedColor = new Color(0.22f, 0.32f, 0.46f, 1f);

        public ClothingConfig Clothing { get; private set; }
        public int Price { get; private set; }

        public void Bind(ClothingConfig clothing, int price, System.Action<FittingRoomCard> onClicked) {
            Clothing = clothing;
            Price = price;

            if (icon != null) {
                icon.sprite = clothing.Icon;
                icon.enabled = clothing.Icon != null;
            }
            if (priceLabel != null) priceLabel.text = $"{price} $";
            if (button != null) {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onClicked?.Invoke(this));
            }
            SetInCart(false);
            SetSelected(false);
        }

        /// <summary>Coche « au panier » (l'article est porté en preview).</summary>
        public void SetInCart(bool inCart) {
            if (checkmark != null) checkmark.SetActive(inCart);
        }

        /// <summary>Surbrillance « sélectionné » (affiché dans le panneau de détail).</summary>
        public void SetSelected(bool selected) {
            if (background != null) background.color = selected ? SelectedColor : NormalColor;
        }
    }
}
