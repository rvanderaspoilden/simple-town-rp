using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Encart « item tenu » du HUD (bas-droite, au-dessus du téléphone) : icône + nom de
    /// l'item actuellement en main, avec en sous-titre le type d'objet — remplacé pour une
    /// arme à feu par la ligne de munitions (chargeur / réserve) poussée par le
    /// <see cref="PlayerShooter"/> local (même flux que CrosshairUI/ScopeOverlayUI).
    /// Structure autorée dans le prefab HUD (Tools ▸ The BROZ ▸ Build Held Item Panel UI) ;
    /// ce composant ne fait que remplir/basculer.
    /// </summary>
    public class HeldItemPanelUI : MonoBehaviour {
        [Header("Références (prefab)")]
        [Tooltip("Racine togglée (enfant) — cachée quand aucune main ne tient d'item.")]
        [SerializeField] private GameObject root;
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [Tooltip("Type d'objet (Consommable, Colis…) — masqué pour une arme à feu.")]
        [SerializeField] private TMP_Text subtitleText;
        [Tooltip("Ligne munitions (icône balles + compteur) — visible seulement arme à feu en main.")]
        [SerializeField] private GameObject ammoRow;
        [SerializeField] private TMP_Text ammoText;

        [Header("Couleurs munitions")]
        [SerializeField] private Color ammoColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color ammoEmptyColor = new Color(1f, 0.35f, 0.3f, 0.95f);

        private string lastAmmoText;

        public static HeldItemPanelUI Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            if (this.root != null) this.root.SetActive(false);
        }

        private void OnEnable() {
            PlayerHands.OnHandChanged += this.Refresh;
            this.Refresh();
        }

        private void OnDisable() {
            PlayerHands.OnHandChanged -= this.Refresh;
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        /// <summary>Re-lit l'item tenu par le joueur local (main droite = seul slot logique ;
        /// la gauche en repli pour les poses deux mains) et re-remplit l'encart.</summary>
        private void Refresh() {
            PlayerHands hands = PlayerController.Local != null ? PlayerController.Local.PlayerHands : null;
            ItemBehaviour held = hands != null ? (hands.RightHandItem != null ? hands.RightHandItem : hands.LeftHandItem) : null;
            ItemConfig cfg = held != null ? held.Configuration : null;

            if (cfg == null) {
                if (this.root != null) this.root.SetActive(false);
                return;
            }

            if (this.iconImage != null) {
                this.iconImage.sprite = cfg.Icon;
                this.iconImage.enabled = cfg.Icon != null;
            }
            if (this.nameText != null) this.nameText.text = cfg.Label;

            // Arme à feu : la ligne de munitions remplace le sous-titre (remplie par le
            // PlayerShooter local à la frame suivante — on blanchit en attendant).
            bool firearm = cfg is WeaponConfig;
            if (this.subtitleText != null) {
                this.subtitleText.gameObject.SetActive(!firearm);
                if (!firearm) this.subtitleText.text = TypeLabel(cfg);
            }
            if (this.ammoRow != null) this.ammoRow.SetActive(firearm);
            if (firearm && this.ammoText != null) {
                this.lastAmmoText = null;
                this.ammoText.text = string.Empty;
            }

            if (this.root != null) this.root.SetActive(true);
        }

        /// <summary>Compteur de munitions « chargeur / réserve » (poussé chaque frame par le
        /// PlayerShooter de l'owner, comme CrosshairUI). -1 = inconnu (en attente serveur).</summary>
        public void SetAmmo(int loaded, int magazineSize, int reserve, bool reloading) {
            if (this.ammoText == null) return;
            string text = reloading ? "Recharge…"
                : (loaded < 0 ? "–" : loaded.ToString()) + " / " + (reserve < 0 ? "–" : reserve.ToString());
            if (text != this.lastAmmoText) {
                this.lastAmmoText = text;
                this.ammoText.text = text;
                this.ammoText.color = !reloading && loaded == 0 ? this.ammoEmptyColor : this.ammoColor;
            }
        }

        /// <summary>Plus d'arme en main : blanchit le compteur (la ligne est déjà masquée par
        /// <see cref="Refresh"/> ; filet de sécurité si les events se croisent).</summary>
        public void ClearAmmo() {
            if (this.ammoText == null) return;
            this.lastAmmoText = null;
            this.ammoText.text = string.Empty;
        }

        /// <summary>Type d'objet affiché en sous-titre (strings UI en français — même
        /// classification que la chip de la carte de détail d'inventaire).</summary>
        private static string TypeLabel(ItemConfig cfg) {
            if (cfg is MeleeWeaponConfig) return "Arme de mêlée";
            if (cfg is AmmoConfig) return "Munitions";
            if (cfg is ClothingConfig clothing) return clothing.Gender == Gender.FEMALE ? "Vêtement femme" : "Vêtement homme";
            ContainerConfig cc = ItemContainerConfig.Of(cfg);
            if (cc != null && cc.IsContainer) return cc.Wearable ? "Sac" : "Conteneur";
            switch (cfg.Type) {
                case ItemType.CONSUMABLE: return "Consommable";
                case ItemType.PACKAGE:    return "Colis";
                case ItemType.WASTE:      return "Déchet";
                case ItemType.CLOTHING:   return "Vêtement";
                default:                  return "Objet divers";
            }
        }
    }
}
