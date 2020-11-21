using System.Collections;
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
            if (!this.freelookCamera.m_LookAt) {
                StartCoroutine(this.SetCameraTarget());
            }
            
            this.freelookCamera.gameObject.SetActive(true);
        }

        private void OnDisable() {
            this.freelookCamera.gameObject.SetActive(false);
        }

        private IEnumerator SetCameraTarget() {
            Debug.Log("Set target camera");
            do {
                if (RoomManager.LocalPlayer) {
                    this.SetTarget(RoomManager.LocalPlayer.GetHeadTargetForCamera());
                } else {
                    Debug.Log("No local player found");
                }
                
                yield return new WaitForSeconds(0.1f);
            } while (!this.freelookCamera.m_LookAt);
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