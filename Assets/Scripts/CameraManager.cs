using System;
using Cinemachine;
using Photon.Pun.Demo.PunBasics;
using Sim.Interactables;
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
            this.ManageInteraction();

            if (Input.GetMouseButtonDown(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 300f;
            }
            
            if (Input.GetMouseButtonUp(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }
        }

        private void ManageInteraction() {
            if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit)) {
                Interactable objectToInteract = null;
                
                if (hit.collider.CompareTag("Interactable")) {
                    Interactable interactable = hit.collider.GetComponentInParent<Interactable>();
                    
                    // todo display cursor for interaction

                    if (interactable.CanInteractWithPlayer(RoomManager.LocalPlayer)) {
                        objectToInteract = interactable;
                    }
                }
                
                if (Input.GetMouseButtonDown(0)) {
                    if (objectToInteract) {
                        objectToInteract.Interact();
                    } else {
                        RoomManager.Instance.MovePlayerTo(hit.point);
                    }
                }
            }
        }

        public void SetCameraTarget(Transform transform) {
            this.freelookCamera.Follow = transform;
            this.freelookCamera.LookAt = transform;
        }
    }
}