using Unity.Cinemachine;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Over-the-shoulder TPS camera mode (CameraModeEnum.TPS) — replicates the feel of Unity's
    /// Starter Assets ThirdPersonController camera: a pivot transform glued to the player's head
    /// carries the mouse-driven yaw/pitch, and a CM3 CinemachineCamera with ThirdPersonFollow
    /// trails it (shoulder offset + built-in obstacle avoidance).
    /// Same lifecycle pattern as ThirdPersonCamera/BuildCamera: CameraManager enables this
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

        private float yaw;
        private float pitch;

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

            // Mouse look only while the gameplay cursor is locked — a freed cursor means some UI
            // is open (radial, phone, inventory...) and the camera must not drift under it.
            if (Cursor.lockState == CursorLockMode.Locked) {
                this.yaw += Input.GetAxis("Mouse X") * this.mouseSensitivity;
                this.pitch -= Input.GetAxis("Mouse Y") * this.mouseSensitivity;
                this.pitch = Mathf.Clamp(this.pitch, this.bottomClamp, this.topClamp);
            }

            this.SnapPivot();
        }

        private void SnapPivot() {
            if (this.cameraPivot == null || PlayerController.Local == null) return;

            Transform head = PlayerController.Local.GetHeadTargetForCamera();
            if (head != null) this.cameraPivot.position = head.position;
            this.cameraPivot.rotation = Quaternion.Euler(this.pitch, this.yaw, 0f);
        }

        /// <summary>Maps a 0..360 euler pitch to the signed -180..180 range used by the clamp.</summary>
        private static float NormalizePitch(float angle) {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
