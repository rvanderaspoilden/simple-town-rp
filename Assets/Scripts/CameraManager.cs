using System;
using Cinemachine;
using UnityEngine;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private CinemachineFreeLook freelookCamera;
        
        [Header("Only for debug")]
        [SerializeField] private new Camera camera;

        private RaycastHit hit;

        public static CameraManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;

            this.camera = GetComponent<Camera>();
        }

        private void Start() {
            this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
        }

        void Update() {
            if (Input.GetMouseButtonDown(0) && Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit)) {
                RoomManager.Instance.MovePlayerTo(hit.point);
            }

            if (Input.GetMouseButtonDown(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 300f;
            }
            
            if (Input.GetMouseButtonUp(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }
        }

        public void SetCameraTarget(Transform transform) {
            this.freelookCamera.Follow = transform;
            this.freelookCamera.LookAt = transform;
        }
    }
}