using TMPro;
using UnityEngine;

namespace Sim.UI {
    /// <summary>
    /// Full-screen sniper scope overlay (black mask with a circular hole + reticle lines),
    /// authored in the HUD prefab — this component only toggles it. Driven by
    /// <see cref="Sim.TpsShoulderCamera"/> while scoped-aiming a WeaponConfig.scopedAim weapon;
    /// CameraManager hides the regular crosshair while it is visible.
    /// </summary>
    public class ScopeOverlayUI : MonoBehaviour {
        [Tooltip("Root of the whole overlay block — toggled by Show/Hide.")]
        [SerializeField]
        private GameObject root;

        [Tooltip("Center red dot — shown while a character (player/NPC) is under the reticle AND the muzzle has a clear line of fire (driven by PlayerShooter).")]
        [SerializeField]
        private GameObject targetDot;

        [Tooltip("Ammo counter ('loaded/magazine' or reload notice) — driven by the local PlayerShooter while a weapon is held, hidden otherwise.")]
        [SerializeField]
        private TextMeshProUGUI ammoText;

        [Tooltip("Ammo counter color while the magazine holds at least one round.")]
        [SerializeField]
        private Color ammoColor = new Color(1f, 1f, 1f, 0.9f);

        [Tooltip("Ammo counter color when the magazine is empty (reload needed).")]
        [SerializeField]
        private Color ammoEmptyColor = new Color(1f, 0.35f, 0.3f, 0.95f);

        private string lastAmmoText;

        public static ScopeOverlayUI Instance;

        /// <summary>True while the scope overlay is on screen (the regular crosshair must hide).</summary>
        public static bool IsScopeVisible => Instance != null && Instance.root != null && Instance.root.activeSelf;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
            this.Hide();
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        public void Show() {
            if (this.root != null && !this.root.activeSelf) this.root.SetActive(true);
        }

        public void Hide() {
            if (this.root != null && this.root.activeSelf) this.root.SetActive(false);
        }

        /// <summary>Red target-lock dot (clear shot on a player/NPC) — set every frame by the
        /// local PlayerShooter; the dot lives under the root so Hide() masks it too.</summary>
        public void SetTargetLock(bool locked) {
            if (this.targetDot != null && this.targetDot.activeSelf != locked) this.targetDot.SetActive(locked);
        }

        /// <summary>Compteur de munitions « chargées/chargeur » (ou « Recharge… ») — poussé
        /// chaque frame par le PlayerShooter local ; vit sous le root, donc masqué hors lunette.</summary>
        public void SetAmmo(int loaded, int magazineSize, bool reloading) {
            if (this.ammoText == null) return;
            string text = reloading ? "Recharge…"
                : (loaded < 0 ? "–/" + magazineSize : loaded + "/" + magazineSize);
            if (text != this.lastAmmoText) {
                this.lastAmmoText = text;
                this.ammoText.text = text;
                this.ammoText.color = !reloading && loaded == 0 ? this.ammoEmptyColor : this.ammoColor;
            }
            if (!this.ammoText.gameObject.activeSelf) this.ammoText.gameObject.SetActive(true);
        }

        /// <summary>Cache le compteur (aucune arme en main).</summary>
        public void ClearAmmo() {
            if (this.ammoText == null || !this.ammoText.gameObject.activeSelf) return;
            this.ammoText.gameObject.SetActive(false);
            this.lastAmmoText = null;
        }
    }
}
