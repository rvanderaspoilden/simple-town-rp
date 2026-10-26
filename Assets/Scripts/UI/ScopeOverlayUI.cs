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
    }
}
