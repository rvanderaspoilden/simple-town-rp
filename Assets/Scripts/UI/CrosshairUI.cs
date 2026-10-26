using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Center-screen crosshair shown in TPS camera mode (authored in the HUD prefab, this
    /// component only toggles/fills it). Shows the reticle, the aimed target's display name
    /// (same relationship-gated name as the old hover tooltip) and the interact-key prompt
    /// when the target is in range. Driven every frame by CameraManager.
    /// </summary>
    public class CrosshairUI : MonoBehaviour {
        [Header("Settings")]
        [Tooltip("Root of the whole crosshair block — toggled off outside TPS mode.")]
        [SerializeField]
        private GameObject root;

        [Tooltip("Reticle image — tinted with activeColor while an interactable is aimed and in range.")]
        [SerializeField]
        private Image reticleImage;

        [Tooltip("Aimed target's display name (hidden when nothing relevant is aimed).")]
        [SerializeField]
        private TextMeshProUGUI targetNameText;

        [Tooltip("Interact prompt block ('E') — visible only when the aimed target is in range.")]
        [SerializeField]
        private GameObject interactPrompt;

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

        [SerializeField]
        private Color defaultColor = new Color(1f, 1f, 1f, 0.6f);

        [SerializeField]
        private Color activeColor = Color.white;

        public static CrosshairUI Instance;

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

        /// <summary>Reticle + target feedback: name label (optional) and, when in range, the prompt.</summary>
        public void Show(string targetName, bool canInteract) {
            this.SetRootVisible(true);
            if (this.reticleImage != null) this.reticleImage.color = canInteract ? this.activeColor : this.defaultColor;
            if (this.targetNameText != null) {
                this.targetNameText.text = targetName ?? string.Empty;
                this.targetNameText.gameObject.SetActive(!string.IsNullOrEmpty(targetName));
            }
            if (this.interactPrompt != null) this.interactPrompt.SetActive(canInteract);
        }

        /// <summary>Reticle only — TPS mode active but nothing aimed.</summary>
        public void ShowDefault() {
            this.Show(null, false);
        }

        /// <summary>Everything off — not in TPS mode, or a UI freed the cursor.</summary>
        public void Hide() {
            this.SetRootVisible(false);
        }

        /// <summary>Red target-lock dot (clear shot on a player/NPC) — set every frame by the
        /// local PlayerShooter; the dot lives under the root so Hide() masks it too.</summary>
        public void SetTargetLock(bool locked) {
            if (this.targetDot != null && this.targetDot.activeSelf != locked) this.targetDot.SetActive(locked);
        }

        /// <summary>Compteur de munitions « chargées/chargeur » (ou « Recharge… ») — poussé
        /// chaque frame par le PlayerShooter local quand une arme est tenue. loaded &lt; 0 =
        /// état encore inconnu (réponse serveur en route).</summary>
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

        private void SetRootVisible(bool visible) {
            GameObject target = this.root != null ? this.root : this.gameObject;
            if (target.activeSelf != visible) target.SetActive(visible);
        }
    }
}
