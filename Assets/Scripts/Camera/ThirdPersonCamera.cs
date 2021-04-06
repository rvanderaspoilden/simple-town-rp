using Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim {
    public class ThirdPersonCamera : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private CinemachineFreeLook freelookCamera;

        [SerializeField]
        private CameraTarget cameraTarget;

        [SerializeField]
        private float maxRotationSpeed;

        [SerializeField]
        private float maxZoomSpeed;

        private void OnEnable() {
            this.freelookCamera.gameObject.SetActive(true);
        }

        private void OnDisable() {
            this.freelookCamera.gameObject.SetActive(false);
        }

        public void Setup(CinemachineFreeLook originCamera) {
            this.SetVirtualCameraRotation(originCamera.m_XAxis.Value, originCamera.m_YAxis.Value);
        }


        private void SetVirtualCameraRotation(float xValue, float yValue) {
            this.freelookCamera.m_XAxis.Value = xValue;
            this.freelookCamera.m_YAxis.Value = yValue;
        }

        public void SetCameraTarget(Transform target) {
            Debug.Log("Set target camera");
            this.cameraTarget.SetTarget(target);
        }

        void Update() {
            this.ManageRotation();
            this.ManageZoom();
        }

        public CinemachineFreeLook GetVirtualCamera() {
            return this.freelookCamera;
        }

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
    }
}