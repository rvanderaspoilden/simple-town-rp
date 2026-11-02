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

        [Tooltip("Label of the action triggered by the interact key — shown next to the key frame (e.g. \"S'asseoir\", \"Ouvrir\", \"Manger\"). Hidden when no label is provided.")]
        [SerializeField]
        private TextMeshProUGUI interactLabelText;

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

        [Header("Weapon reticle (ranged weapon held)")]
        [Tooltip("Weapon reticle block (brackets + center dot) — shown INSTEAD of the default reticle while a ranged weapon is held (driven by PlayerShooter).")]
        [SerializeField]
        private GameObject weaponReticleRoot;

        [Tooltip("Left bracket — pushed away from center by the spread bloom.")]
        [SerializeField]
        private RectTransform weaponBracketLeft;

        [Tooltip("Right bracket (mirrored sprite) — pushed away from center by the spread bloom.")]
        [SerializeField]
        private RectTransform weaponBracketRight;

        [Tooltip("Every image of the weapon reticle — tinted together (interact color + shot flash).")]
        [SerializeField]
        private Image[] weaponReticleImages;

        [Tooltip("Half-gap (px) between center and each bracket in hip fire.")]
        [SerializeField]
        private float bracketSpacing = 16f;

        [Tooltip("Half-gap (px) while aiming — tighter, mirrors the reduced spread.")]
        [SerializeField]
        private float bracketAimSpacing = 10f;

        [Tooltip("Lerp speed between hip and aim spacing.")]
        [SerializeField]
        private float spacingLerpSpeed = 14f;

        [Tooltip("Extra gap (px) kicked per shot, scaled by the weapon's recoil kick.")]
        [SerializeField]
        private float shotBloom = 9f;

        [Tooltip("Max accumulated bloom (px) — full-auto bursts cap here.")]
        [SerializeField]
        private float maxBloom = 22f;

        [Tooltip("Bloom recovery speed (px/s).")]
        [SerializeField]
        private float bloomRecoverSpeed = 55f;

        [Tooltip("Reticle tint flashed at each shot, fading back to the normal tint.")]
        [SerializeField]
        private Color shotFlashColor = new Color(1f, 0.75f, 0.25f, 1f);

        [Tooltip("Shot flash fade-out speed (1/s).")]
        [SerializeField]
        private float flashDecaySpeed = 7f;

        // Weapon-reticle runtime state (owner-driven by PlayerShooter).
        private bool weaponMode;
        private bool weaponAiming;
        private float currentSpacing;
        private float bloom;
        private float shotFlash;
        private Color reticleTint;

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
            this.reticleTint = this.defaultColor;
            this.currentSpacing = this.bracketSpacing;
            this.Hide();
        }

        private void Update() {
            if (!this.weaponMode || this.weaponReticleRoot == null || !this.weaponReticleRoot.activeInHierarchy) return;

            // Écart des crochets : resserré en visée, élargi d'un bloom à chaque tir.
            float targetSpacing = this.weaponAiming ? this.bracketAimSpacing : this.bracketSpacing;
            this.currentSpacing = Mathf.Lerp(this.currentSpacing, targetSpacing, Time.deltaTime * this.spacingLerpSpeed);
            this.bloom = Mathf.MoveTowards(this.bloom, 0f, Time.deltaTime * this.bloomRecoverSpeed);
            float gap = this.currentSpacing + this.bloom;
            if (this.weaponBracketLeft != null) this.weaponBracketLeft.anchoredPosition = new Vector2(-gap, 0f);
            if (this.weaponBracketRight != null) this.weaponBracketRight.anchoredPosition = new Vector2(gap, 0f);

            // Flash du tir fondu vers la teinte courante (couleur d'interaction).
            if (this.shotFlash > 0f) this.shotFlash = Mathf.MoveTowards(this.shotFlash, 0f, Time.deltaTime * this.flashDecaySpeed);
            this.ApplyWeaponTint();
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        /// <summary>Reticle + target feedback: name label (optional), and — when in range — the
        /// interact key frame followed by the action's label (<paramref name="actionLabel"/>, e.g.
        /// the aimed prop's priority action). A null/empty label falls back to the bare key prompt.</summary>
        public void Show(string targetName, bool canInteract, string actionLabel = null) {
            this.SetRootVisible(true);
            this.reticleTint = canInteract ? this.activeColor : this.defaultColor;
            if (this.reticleImage != null) this.reticleImage.color = this.reticleTint;
            if (this.weaponMode) this.ApplyWeaponTint();
            if (this.targetNameText != null) {
                this.targetNameText.text = targetName ?? string.Empty;
                this.targetNameText.gameObject.SetActive(!string.IsNullOrEmpty(targetName));
            }
            if (this.interactPrompt != null) this.interactPrompt.SetActive(canInteract);
            if (this.interactLabelText != null) {
                bool showLabel = canInteract && !string.IsNullOrEmpty(actionLabel);
                this.interactLabelText.text = actionLabel ?? string.Empty;
                if (this.interactLabelText.gameObject.activeSelf != showLabel)
                    this.interactLabelText.gameObject.SetActive(showLabel);
            }
        }

        /// <summary>Reticle only — TPS mode active but nothing aimed.</summary>
        public void ShowDefault() {
            this.Show(null, false, null);
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

        /// <summary>Bascule réticule par défaut ↔ réticule d'arme (crochets + point) — poussé
        /// chaque frame par le PlayerShooter local : arme à distance en main = réticule d'arme,
        /// resserré pendant la visée (la dispersion y est réduite).</summary>
        public void SetWeaponState(bool weaponHeld, bool aiming) {
            this.weaponAiming = aiming;
            if (this.weaponMode == weaponHeld) return;
            this.weaponMode = weaponHeld;
            if (this.reticleImage != null && this.reticleImage.gameObject.activeSelf == weaponHeld)
                this.reticleImage.gameObject.SetActive(!weaponHeld);
            if (this.weaponReticleRoot != null && this.weaponReticleRoot.activeSelf != weaponHeld)
                this.weaponReticleRoot.SetActive(weaponHeld);
            if (weaponHeld) {
                // Entrée propre : pas de bloom/flash hérités du port d'arme précédent.
                this.bloom = 0f;
                this.shotFlash = 0f;
                this.currentSpacing = this.bracketSpacing;
                this.ApplyWeaponTint();
            }
        }

        /// <summary>Feedback de tir sur le réticule d'arme : les crochets s'écartent (bloom,
        /// proportionnel au kick de recul de l'arme) et flashent, puis reviennent. Appelé par
        /// le PlayerShooter local au départ de chaque coup.</summary>
        public void NotifyShot(float recoilKick) {
            if (!this.weaponMode) return;
            this.bloom = Mathf.Min(this.maxBloom, this.bloom + this.shotBloom * Mathf.Max(0.2f, recoilKick));
            this.shotFlash = 1f;
        }

        /// <summary>Teinte courante (interaction) mélangée au flash de tir, appliquée à toutes
        /// les images du réticule d'arme.</summary>
        private void ApplyWeaponTint() {
            if (this.weaponReticleImages == null) return;
            Color color = this.shotFlash > 0f
                ? Color.Lerp(this.reticleTint, this.shotFlashColor, this.shotFlash)
                : this.reticleTint;
            foreach (Image image in this.weaponReticleImages) {
                if (image != null) image.color = color;
            }
        }

        /// <summary>Compteur de munitions « chargées/chargeur | réserve » (ou « Recharge… »)
        /// — poussé chaque frame par le PlayerShooter local quand une arme est tenue.
        /// loaded/reserve &lt; 0 = valeur encore inconnue (réponse serveur en route) :
        /// le chargeur s'affiche « – », la réserve est omise.</summary>
        public void SetAmmo(int loaded, int magazineSize, int reserve, bool reloading) {
            if (this.ammoText == null) return;
            string text = reloading ? "Recharge…"
                : (loaded < 0 ? "–" : loaded.ToString()) + "/" + magazineSize
                  + (reserve >= 0 ? " | " + reserve : "");
            if (text != this.lastAmmoText) {
                this.lastAmmoText = text;
                this.ammoText.text = text;
                // Rouge : chargeur vide ; une réserve à 0 en plus = plus rien à tirer du tout.
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
