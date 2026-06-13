using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim {
    /// <summary>
    /// Caméra Cinemachine dédiée à la conduite. Calquée sur <see cref="ThirdPersonCamera"/> :
    /// un <see cref="CinemachineFreeLook"/> propre qui suit l'ancre caméra du véhicule, orbitable
    /// UNIQUEMENT à la molette enfoncée (jamais via WASD/ZQSD — le braquage du véhicule ne doit pas
    /// tourner la vue). Ajoute un effet de vitesse : le FOV s'élargit avec la vitesse normalisée.
    ///
    /// L'ancre suivie (<see cref="VehicleController.CameraAnchor"/>) est recentrée sur l'axe vertical
    /// du véhicule, donc le braquage ne déplace pas la cible (pas de "swing").
    /// </summary>
    public class DriveCamera : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private CinemachineFreeLook freelookCamera;

        [SerializeField]
        private float maxRotationSpeed;

        [Header("Speed effect")]
        [Tooltip("Élargissement maximal du FOV (degrés) à pleine vitesse.")]
        [SerializeField] private float maxFovBoost = 12f;
        [Tooltip("Vitesse de lerp du FOV (plus grand = plus réactif).")]
        [SerializeField] private float fovLerpSpeed = 4f;

        private VehicleController vehicle;
        private float _baseFov;
        private bool _baseFovCaptured;

        private void OnEnable() {
            this.freelookCamera.gameObject.SetActive(true);
            if (!_baseFovCaptured) {
                this._baseFov = this.freelookCamera.m_Lens.FieldOfView;
                this._baseFovCaptured = true;
            }
        }

        private void OnDisable() {
            this.freelookCamera.m_Lens.FieldOfView = this._baseFov;
            this.freelookCamera.gameObject.SetActive(false);
        }

        public void SetVehicle(VehicleController target) {
            this.vehicle = target;
            Transform anchor = target != null ? target.CameraAnchor : null;
            this.freelookCamera.Follow = anchor;
            this.freelookCamera.LookAt = anchor;
        }

        public CinemachineFreeLook GetVirtualCamera() {
            return this.freelookCamera;
        }

        private void Update() {
            this.ManageRotation();
            this.ManageZoom();
            this.ManageSpeedFov();
        }

        // Rotation à la molette enfoncée uniquement (identique à ThirdPersonCamera).
        private void ManageRotation() {
            if (Input.GetMouseButtonDown(2) && !EventSystem.current.IsPointerOverGameObject()) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = this.maxRotationSpeed;
            }

            if (Input.GetMouseButtonUp(2)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }
        }

        private void ManageZoom() {
            this.freelookCamera.m_YAxis.m_InputAxisName = !EventSystem.current.IsPointerOverGameObject() ? "Mouse ScrollWheel" : string.Empty;
        }

        private void ManageSpeedFov() {
            if (!_baseFovCaptured) return;
            float target = this._baseFov + this.maxFovBoost * (this.vehicle != null ? this.vehicle.NormalizedSpeed : 0f);
            this.freelookCamera.m_Lens.FieldOfView =
                Mathf.Lerp(this.freelookCamera.m_Lens.FieldOfView, target, this.fovLerpSpeed * Time.deltaTime);
        }
    }
}
