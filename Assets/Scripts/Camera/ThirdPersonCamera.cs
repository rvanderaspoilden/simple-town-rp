using System.Collections;
using Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sim {
    public class ThirdPersonCamera : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private CinemachineFreeLook freelookCamera;

        [SerializeField]
        private float maxRotationSpeed;

        private Coroutine setCameraTargetCoroutine;

        private void OnEnable() {
            if (!SceneManager.GetActiveScene().name.Equals("Launcher") && !this.freelookCamera.m_LookAt && this.setCameraTargetCoroutine == null) {
                this.setCameraTargetCoroutine = StartCoroutine(this.SetCameraTarget());
            }

            this.freelookCamera.gameObject.SetActive(true);

            SceneManager.sceneLoaded += this.SceneLoaded;
        }

        private void OnDisable() {
            this.freelookCamera.gameObject.SetActive(false);
            
            SceneManager.sceneLoaded -= this.SceneLoaded;
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) {
            if (!scene.name.Equals("Launcher") && !this.freelookCamera.m_LookAt && this.setCameraTargetCoroutine == null) {
                this.setCameraTargetCoroutine = StartCoroutine(this.SetCameraTarget());
            }
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

            this.setCameraTargetCoroutine = null;
        }

        void Update() {
            this.ManageRotation();
        }

        public CinemachineFreeLook GetVirtualCamera() {
            return this.freelookCamera;
        }

        private void SetTarget(Transform target) {
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