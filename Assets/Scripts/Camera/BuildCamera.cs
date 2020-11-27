using Cinemachine;
using DG.Tweening;
using Sim.Enums;
using UnityEngine;

namespace Sim {
    public class BuildCamera : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Transform target;

        [SerializeField]
        private float moveSpeedWithKeyboard;

        [SerializeField]
        private float dragSpeed;

        [SerializeField]
        private float maxRotationSpeed;

        [SerializeField]
        private CinemachineFreeLook freelookCamera;

        private new Camera camera;

        private float horizontal;
        private float vertical;

        private Vector3 dragOrigin;

        private void Awake() {
            this.camera = Camera.main;
        }

        private void OnEnable() {
            BuildManager.OnModeChanged += this.BuildModeChanged;

            this.freelookCamera.gameObject.SetActive(true);
        }

        private void OnDisable() {
            BuildManager.OnModeChanged -= this.BuildModeChanged;

            this.freelookCamera.gameObject.SetActive(false);
        }

        void Update() {
            this.ManageRotation();

            if (BuildManager.Instance.GetMode() != BuildModeEnum.VALIDATING) {
                if (Input.GetMouseButtonDown(2)) {
                    this.dragOrigin = Input.mousePosition;
                } else {
                    this.ManageMovementWithKeyboard();
                }

                if (Input.GetMouseButton(2)) {
                    this.ManageDragCamera();
                }
            }
        }

        public void Setup(CinemachineFreeLook originCamera) {
            this.SetVirtualCameraRotation(originCamera.m_XAxis.Value, originCamera.m_YAxis.Value);
            this.SetTargetPosition(RoomManager.LocalPlayer.transform.position, false);
        }

        /**
         * Use to set camera target at specific position
         */
        private void SetTargetPosition(Vector3 pos, bool smooth) {
            if (smooth) {
                this.target.DOMove(pos, 0.5f);
            } else {
                this.target.transform.position = pos;
            }
        }

        private void SetVirtualCameraRotation(float xValue, float yValue) {
            this.freelookCamera.m_XAxis.Value = xValue;
            this.freelookCamera.m_YAxis.Value = yValue;
        }

        private void BuildModeChanged(BuildModeEnum mode) {
            if (mode == BuildModeEnum.VALIDATING) {
                this.SetTargetPosition(BuildManager.Instance.GetCurrentPreviewedProps().transform.position, true);
            }
        }

        private void ManageRotation() {
            if (Input.GetMouseButtonDown(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = this.maxRotationSpeed;
            }

            if (Input.GetMouseButtonUp(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }
        }

        private void ManageMovementWithKeyboard() {
            this.horizontal = Input.GetAxis("Horizontal");
            this.vertical = Input.GetAxis("Vertical");

            Vector3 movement = this.camera.transform.TransformDirection(new Vector3(this.horizontal, 0, this.vertical));

            this.target.Translate(new Vector3(movement.x, 0, movement.z) * this.moveSpeedWithKeyboard * Time.deltaTime);
        }

        private void ManageDragCamera() {
            Vector3 pos = Camera.main.ScreenToViewportPoint(Input.mousePosition - dragOrigin);
            Vector3 move = this.camera.transform.TransformDirection(new Vector3(pos.x, 0, pos.y));

            this.target.Translate(new Vector3(move.x, 0, move.z) * dragSpeed * Time.deltaTime, Space.World);
        }
    }
}