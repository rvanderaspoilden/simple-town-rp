using System.Collections;
using Cinemachine;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sim {
    public class ThirdPersonCamera : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private CinemachineFreeLook freelookCamera;

        [SerializeField]
        private CameraTarget cameraTarget;

        [SerializeField]
        private float maxRotationSpeed;

        private Coroutine setCameraTargetCoroutine;

        private void OnEnable() {
            if (!SceneManager.GetActiveScene().name.Equals("Launcher") && this.setCameraTargetCoroutine == null) {
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
            if (!SceneManager.GetActiveScene().name.Equals("Launcher") && this.setCameraTargetCoroutine == null) {
                this.setCameraTargetCoroutine = StartCoroutine(this.SetCameraTarget());
            }
        }

        public void Setup(CinemachineFreeLook originCamera) {
            this.SetVirtualCameraRotation(originCamera.m_XAxis.Value, originCamera.m_YAxis.Value);
        }


        private void SetVirtualCameraRotation(float xValue, float yValue) {
            this.freelookCamera.m_XAxis.Value = xValue;
            this.freelookCamera.m_YAxis.Value = yValue;
        }

        private IEnumerator SetCameraTarget() {
            Debug.Log("Set target camera");

            do {
                if (RoomManager.LocalCharacter) {
                    this.cameraTarget.SetTarget(RoomManager.LocalCharacter.GetHeadTargetForCamera());
                } else {
                    Debug.Log("No local player found");
                }

                yield return new WaitForSeconds(0.1f);
            } while (!this.cameraTarget.GetTarget());

            this.setCameraTargetCoroutine = null;
        }

        void Update() {
            this.ManageRotation();
        }

        public CinemachineFreeLook GetVirtualCamera() {
            return this.freelookCamera;
        }

        private void ManageRotation() {
            if (Input.GetMouseButtonDown(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = this.maxRotationSpeed;

                HUDManager.Instance.DisplayContextMenu(false);
            }

            if (Input.GetMouseButtonUp(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }
        }
    }
}