using System.Collections.Generic;
using Sim.UI;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sim {
    /// <summary>
    /// Over-the-shoulder TPS camera mode (CameraModeEnum.TPS) — replicates the feel of Unity's
    /// Starter Assets ThirdPersonController camera: a pivot transform glued to the player's head
    /// carries the mouse-driven yaw/pitch, and a CM3 CinemachineCamera with ThirdPersonFollow
    /// trails it (shoulder offset + built-in obstacle avoidance).
    /// Same lifecycle pattern as BuildCamera: CameraManager enables this
    /// component per mode, and OnEnable/OnDisable toggles the virtual-camera GameObject so the
    /// CinemachineBrain blends to/from it.
    /// </summary>
    public class TpsShoulderCamera : MonoBehaviour {
        [Header("Settings")]
        [Tooltip("CM3 virtual camera (child 'TPS Virtual Camera'), body = ThirdPersonFollow, Follow = pivot.")]
        [SerializeField]
        private CinemachineCamera virtualCamera;

        [Tooltip("Pivot (child 'TPS Camera Pivot') glued to the player's head; carries the look yaw/pitch.")]
        [SerializeField]
        private Transform cameraPivot;

        [Tooltip("Degrees of look rotation per mouse-axis unit.")]
        [SerializeField]
        private float mouseSensitivity = 1.5f;

        [Tooltip("Pitch clamp, Starter Assets values: cannot look further down than this.")]
        [SerializeField]
        private float bottomClamp = -30f;

        [Tooltip("Pitch clamp, Starter Assets values: cannot look further up than this.")]
        [SerializeField]
        private float topClamp = 70f;

        [Header("Visée (zoom arme)")]
        [Tooltip("FOV (deg) pendant la visée d'une arme — fondu depuis le FOV par défaut de la lens (40 sur la vcam TPS), retour auto en fin de visée.")]
        [SerializeField]
        private float aimFov = 30f;

        [Tooltip("Vitesse du fondu de FOV (deg/s).")]
        [SerializeField]
        private float aimZoomSpeed = 120f;

        [Range(0.1f, 1f)]
        [Tooltip("Facteur de sensibilité souris pendant la visée (zoomé → gestes plus fins).")]
        [SerializeField]
        private float aimSensitivityFactor = 0.6f;

        [Header("Visée lunette (arme WeaponConfig.scopedAim)")]
        [Tooltip("Vitesse du fondu TPS ↔ œil (0→1 par s) : la caméra glisse de l'épaule jusqu'à l'œil en entrant à la lunette. FOV et sensibilité de lunette viennent de la WeaponConfig (scopeFov / scopeSensitivityFactor).")]
        [SerializeField]
        private float scopeBlendSpeed = 5f;

        [Range(0f, 1f)]
        [Tooltip("Seuil du fondu au-delà duquel le corps du joueur local (et son arme) passe en ombre seule — la caméra est quasi dans la tête.")]
        [SerializeField]
        private float scopeHideBodyThreshold = 0.6f;

        private float yaw;
        private float pitch;
        private float defaultFov = -1f;
        private PlayerShooter localShooter;

        // Lunette : fondu épaule→œil appliqué sur le ThirdPersonFollow de la vcam.
        private CinemachineThirdPersonFollow follow;
        private float defaultCameraDistance;
        private Vector3 defaultShoulderOffset;
        private float defaultVerticalArmLength;
        private Vector3 defaultDamping;
        private float scopeBlend;
        // Hauteur de l'œil au-dessus du ROOT du joueur, figée à l'entrée en lunette : la
        // caméra scopée s'ancre sur le root (stable) et non sur l'os de tête animé, sinon la
        // respiration/idle se transmet à la vue — insupportable à 8° de FOV.
        private float scopeEyeHeight = -1f;
        private bool scopeEntered;
        private bool bodyHidden;
        private readonly List<KeyValuePair<Renderer, ShadowCastingMode>> hiddenRenderers = new();

        private void OnEnable() {
            if (this.virtualCamera != null) this.virtualCamera.gameObject.SetActive(true);
        }

        private void OnDisable() {
            if (this.virtualCamera != null) this.virtualCamera.gameObject.SetActive(false);
            // Sortie de mode en pleine lunette (véhicule, build...) : la vcam doit rendre la
            // main avec sa pose d'épaule d'origine et le corps redevenir visible.
            this.scopeBlend = 0f;
            this.scopeEntered = false;
            this.ApplyScopeRig();
            this.SetLocalBodyHidden(false);
            if (ScopeOverlayUI.Instance != null) ScopeOverlayUI.Instance.Hide();
        }

        /// <summary>Current camera yaw (deg) — the heading ZQSD movement is relative to.</summary>
        public float Yaw => this.yaw;

        /// <summary>Called by CameraManager when entering TPS mode: seeds yaw/pitch from the
        /// rendering camera's current orientation so the brain blend starts from a matching pose.</summary>
        public void Setup() {
            Transform cam = CameraManager.Instance != null ? CameraManager.Instance.Camera.transform : null;
            if (cam != null) {
                Vector3 euler = cam.eulerAngles;
                this.yaw = euler.y;
                this.pitch = Mathf.Clamp(NormalizePitch(euler.x), this.bottomClamp, this.topClamp);
            }

            this.SnapPivot();
        }

        private void LateUpdate() {
            if (PlayerController.Local == null) return;

            bool aiming = this.IsLocalPlayerAiming();
            // Arme à lunette (sniper) : la visée passe en FPS — FOV et sensibilité de la config.
            WeaponConfig scopedCfg = aiming ? this.ScopedWeaponConfig : null;

            // Mouse look only while the gameplay cursor is locked — a freed cursor means some UI
            // is open (radial, phone, inventory...) and the camera must not drift under it.
            if (Cursor.lockState == CursorLockMode.Locked) {
                float aimFactor = scopedCfg != null ? scopedCfg.ScopeSensitivityFactor : this.aimSensitivityFactor;
                float sensitivity = this.mouseSensitivity * (aiming ? aimFactor : 1f);
                this.yaw += Input.GetAxis("Mouse X") * sensitivity;
                this.pitch -= Input.GetAxis("Mouse Y") * sensitivity;
                this.pitch = Mathf.Clamp(this.pitch, this.bottomClamp, this.topClamp);
            }

            // Le fondu de lunette AVANT SnapPivot : l'ancrage œil du pivot dépend du blend courant.
            this.TickScope(scopedCfg != null);
            this.SnapPivot();
            this.TickAimZoom(aiming, scopedCfg);
        }

        /// <summary>Zoom immersif de visée : le FOV de la virtual camera fond vers aimFov
        /// (ou le scopeFov de l'arme à lunette) pendant la visée et revient au FOV d'origine
        /// de la lens sinon.</summary>
        private void TickAimZoom(bool aiming, WeaponConfig scopedCfg) {
            if (this.virtualCamera == null) return;
            if (this.defaultFov <= 0f) this.defaultFov = this.virtualCamera.Lens.FieldOfView;

            float target = aiming ? (scopedCfg != null ? scopedCfg.ScopeFov : this.aimFov) : this.defaultFov;
            // La lunette couvre une plage de FOV bien plus large que le zoom épaule : vitesse
            // proportionnelle à l'écart pour que l'entrée/sortie reste à durée comparable.
            float speed = this.aimZoomSpeed * (scopedCfg != null || this.scopeBlend > 0.001f ? 3f : 1f);
            this.virtualCamera.Lens.FieldOfView = Mathf.MoveTowards(
                this.virtualCamera.Lens.FieldOfView, target, Time.deltaTime * speed);
        }

        /// <summary>Config de l'arme tenue si c'est une arme à lunette (scopedAim), sinon null.</summary>
        private WeaponConfig ScopedWeaponConfig {
            get {
                WeaponBehaviour weapon = this.localShooter != null ? this.localShooter.HeldWeapon : null;
                WeaponConfig cfg = weapon != null ? weapon.WeaponConfiguration : null;
                return cfg != null && cfg.ScopedAim ? cfg : null;
            }
        }

        /// <summary>Fondu épaule → œil de la lunette : le ThirdPersonFollow de la vcam glisse
        /// vers distance/épaule zéro (la caméra finit SUR le pivot, collé à la tête) ; le corps
        /// du joueur local passe en ombre seule au-delà du seuil pour ne pas voir l'intérieur
        /// du crâne. Purement local — les copies distantes gardent la posture de visée TPS.</summary>
        private void TickScope(bool scoped) {
            // Hauteur d'œil (re)capturée à chaque ENTRÉE en lunette — la pose de la tête au
            // moment de viser fait foi, ensuite elle est figée (mouse-only).
            if (scoped && !this.scopeEntered && PlayerController.Local != null) {
                Transform head = PlayerController.Local.GetCameraFollowTarget();
                if (head != null) {
                    this.scopeEyeHeight = head.position.y - PlayerController.Local.transform.position.y;
                }
            }
            this.scopeEntered = scoped;

            float target = scoped ? 1f : 0f;
            if (!Mathf.Approximately(this.scopeBlend, target)) {
                this.scopeBlend = Mathf.MoveTowards(this.scopeBlend, target, Time.deltaTime * this.scopeBlendSpeed);
                this.ApplyScopeRig();
            }

            // Corps masqué ET overlay de lunette sur le même seuil : le fondu noir de
            // l'overlay couvre la transition épaule→œil.
            bool engaged = this.scopeBlend > this.scopeHideBodyThreshold;
            this.SetLocalBodyHidden(engaged);
            if (ScopeOverlayUI.Instance != null) {
                if (engaged) ScopeOverlayUI.Instance.Show();
                else ScopeOverlayUI.Instance.Hide();
            }
        }

        /// <summary>Applique le fondu de lunette courant sur le ThirdPersonFollow (les valeurs
        /// d'épaule d'origine de la vcam sont capturées au premier appel).</summary>
        private void ApplyScopeRig() {
            if (this.follow == null) {
                this.follow = this.virtualCamera != null
                    ? this.virtualCamera.GetComponent<CinemachineThirdPersonFollow>() : null;
                if (this.follow == null) return;
                this.defaultCameraDistance = this.follow.CameraDistance;
                this.defaultShoulderOffset = this.follow.ShoulderOffset;
                this.defaultVerticalArmLength = this.follow.VerticalArmLength;
                this.defaultDamping = this.follow.Damping;
            }

            this.follow.CameraDistance = Mathf.Lerp(this.defaultCameraDistance, 0f, this.scopeBlend);
            this.follow.ShoulderOffset = Vector3.Lerp(this.defaultShoulderOffset, Vector3.zero, this.scopeBlend);
            this.follow.VerticalArmLength = Mathf.Lerp(this.defaultVerticalArmLength, 0f, this.scopeBlend);
            // Damping coupé à la lunette : le moindre retard de suivi se lit comme un bougé
            // parasite au zoom fort — la caméra ne doit répondre qu'à la souris.
            this.follow.Damping = Vector3.Lerp(this.defaultDamping, Vector3.zero, this.scopeBlend);
        }

        /// <summary>Masque/révèle le joueur local (corps + items tenus) pour la vue lunette :
        /// les renderers passent en ShadowsOnly (l'ombre portée reste au sol) et retrouvent
        /// leur mode d'origine en sortant. La liste est recapturée à chaque entrée — l'arme
        /// tenue peut changer entre deux visées.</summary>
        private void SetLocalBodyHidden(bool hidden) {
            if (hidden == this.bodyHidden) return;
            this.bodyHidden = hidden;

            if (hidden) {
                if (PlayerController.Local == null) return;
                this.hiddenRenderers.Clear();
                foreach (Renderer r in PlayerController.Local.GetComponentsInChildren<Renderer>()) {
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    this.hiddenRenderers.Add(new KeyValuePair<Renderer, ShadowCastingMode>(r, r.shadowCastingMode));
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            } else {
                foreach (KeyValuePair<Renderer, ShadowCastingMode> entry in this.hiddenRenderers) {
                    // Une arme lâchée pendant la lunette a quitté la hiérarchie mais vit
                    // toujours : on lui rend quand même son mode d'origine.
                    if (entry.Key != null) entry.Key.shadowCastingMode = entry.Value;
                }
                this.hiddenRenderers.Clear();
            }
        }

        private bool IsLocalPlayerAiming() {
            // Référence re-résolue au respawn (le PlayerController.Local change d'instance).
            if (this.localShooter == null || this.localShooter.gameObject != PlayerController.Local.gameObject) {
                this.localShooter = PlayerController.Local.GetComponent<PlayerShooter>();
            }
            return this.localShooter != null && this.localShooter.IsAiming;
        }

        private void SnapPivot() {
            if (this.cameraPivot == null || PlayerController.Local == null) return;

            Transform head = PlayerController.Local.GetCameraFollowTarget();
            Vector3 position = head != null ? head.position : this.cameraPivot.position;

            // Lunette : l'œil est ancré sur le ROOT du joueur (hauteur figée à l'entrée) —
            // l'os de tête animé (respiration, idle, tourne-corps) ne transmet plus son
            // bougé ; la caméra ne bouge qu'avec la souris et le déplacement réel du joueur.
            if (this.scopeBlend > 0f && this.scopeEyeHeight > 0f) {
                Vector3 eye = PlayerController.Local.transform.position + Vector3.up * this.scopeEyeHeight;
                position = Vector3.Lerp(position, eye, this.scopeBlend);
            }

            this.cameraPivot.position = position;
            this.cameraPivot.rotation = Quaternion.Euler(this.pitch, this.yaw, 0f);
        }

        /// <summary>Maps a 0..360 euler pitch to the signed -180..180 range used by the clamp.</summary>
        private static float NormalizePitch(float angle) {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
