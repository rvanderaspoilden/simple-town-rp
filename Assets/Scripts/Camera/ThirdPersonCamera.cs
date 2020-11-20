using Cinemachine;
using UnityEngine;

namespace Sim {
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField]
        private CinemachineFreeLook freelookCamera;

        [SerializeField]
        private float maxRotationSpeed;
        
        private void OnEnable() {
            if (this.freelookCamera.LookAt == null && RoomManager.LocalPlayer) {
                this.SetTarget(RoomManager.LocalPlayer.GetHeadTargetForCamera());
            }
            
            this.freelookCamera.gameObject.SetActive(true);
        }

        private void OnDisable() {
            this.freelookCamera.gameObject.SetActive(false);
        }

        void Update()
        {
            this.ManageRotation();
        }

        public CinemachineFreeLook GetVirtualCamera() {
            return this.freelookCamera;
        }

        public void SetTarget(Transform target) {
            this.freelookCamera.Follow = target;
            this.freelookCamera.LookAt = target;
        }

        private void ManageRotation() {
            if (Input.GetMouseButtonDown(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = this.maxRotationSpeed;
            }

            if (Input.GetMouseButtonUp(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }
        }
    }
}