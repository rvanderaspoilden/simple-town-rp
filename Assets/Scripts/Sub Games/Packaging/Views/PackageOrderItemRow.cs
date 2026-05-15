using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Une ligne dans le panel "Commande" : icone + nom + "Nx".
    /// Pose son propre RectTransform — destiné à être enfant d'un
    /// VerticalLayoutGroup.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PackageOrderItemRow : MonoBehaviour {
        [SerializeField] private Image icon;
        [SerializeField] private TextMeshProUGUI nameLabel;
        [SerializeField] private TextMeshProUGUI quantityLabel;
        [SerializeField] private Image fragileBadge;
        [SerializeField] private Image heavyBadge;
        [SerializeField] private Image checkmark;

        public void Bind(PackageItemDefinition def, int quantity) {
            if (icon != null) {
                icon.sprite = def.icon;
                icon.color = def.tint;
            }
            if (nameLabel != null) nameLabel.text = def.displayName;
            if (quantityLabel != null) quantityLabel.text = $"{quantity}x";
            if (fragileBadge != null) fragileBadge.gameObject.SetActive(def.fragile);
            if (heavyBadge != null) heavyBadge.gameObject.SetActive(def.heavy);
            if (checkmark != null) checkmark.gameObject.SetActive(false);
        }

        public void SetPlacedCount(int placed, int total) {
            if (checkmark != null) checkmark.gameObject.SetActive(placed >= total);
            if (quantityLabel != null) {
                quantityLabel.text = placed >= total
                    ? $"{total}x"
                    : $"{placed}/{total}";
            }
        }
    }
}
