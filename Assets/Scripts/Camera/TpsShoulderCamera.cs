using Unity.Cinemachine;
using UnityEngine;

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

        private float yaw;
        private float pitch;
        private float defaultFov = -1f;
        private PlayerShooter localShooter;

        private void OnEnable() {
            if (this.virtualCamera != null) this.virtualCamera.gameObject.SetActive(true);
        }

        private void OnDisable() {
            if (this.virtualCamera != null) this.virtualCamera.gameObject.SetActive(false);
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

            // Mouse look only while the gameplay cursor is locked — a freed cursor means some UI
            // is open (radial, phone, inventory...) and the camera must not drift under it.
            if (Cursor.lockState == CursorLockMode.Locked) {
                float sensitivity = this.mouseSensitivity * (aiming ? this.aimSensitivityFactor : 1f);
                this.yaw += Input.GetAxis("Mouse X") * sensitivity;
                this.pitch -= Input.GetAxis("Mouse Y") * sensitivity;
                this.pitch = Mathf.Clamp(this.pitch, this.bottomClamp, this.topClamp);
            }

            this.SnapPivot();
            this.TickAimZoom(aiming);
        }

        /// <summary>Zoom immersif de visée : le FOV de la virtual camera fond vers aimFov
        /// pendant la visée et revient au FOV d'origine de la lens sinon.</summary>
        private void TickAimZoom(bool aiming) {
            if (this.virtualCamera == null) return;
            if (this.defaultFov <= 0f) this.defaultFov = this.virtualCamera.Lens.FieldOfView;

            float target = aiming ? this.aimFov : this.defaultFov;
            this.virtualCamera.Lens.FieldOfView = Mathf.MoveTowards(
                this.virtualCamera.Lens.FieldOfView, target, Time.deltaTime * this.aimZoomSpeed);
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
            if (head != null) this.cameraPivot.position = head.position;
            this.cameraPivot.rotation = Quaternion.Euler(this.pitch, this.yaw, 0f);
        }

        /// <summary>Maps a 0..360 euler pitch to the signed -180..180 range used by the clamp.</summary>
        private static float NormalizePitch(float angle) {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
