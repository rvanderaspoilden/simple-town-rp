﻿using System;
using System.Linq;
using Cinemachine;
using Photon.Pun;
using Photon.Pun.Demo.PunBasics;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private CinemachineFreeLook freelookCamera;

        [SerializeField] private VirtualCameraFollow virtualCameraFollow;
        [SerializeField] private LayerMask layerMaskInFreeMode;
        [SerializeField] private LayerMask layerMaskTransparent;

        [Header("Only for debug")]
        [SerializeField] private new Camera camera;

        [SerializeField] private CameraModeEnum currentMode;
        [SerializeField] private GameObject currentNearWall;

        [Header("Build settings")]
        [SerializeField] private float cameraRotationSpeed = 3f;

        [SerializeField] private float cameraMoveSpeed = 3f;
        [SerializeField] private float cameraZoomSpeed = 3f;

        [SerializeField] private float propsRotationSpeed = 1.5f;
        [SerializeField] private float propsStepSize = 0.1f;
        [SerializeField] private GameObject propsToInstantiate;

        [SerializeField] private Material errorMaterial;

        [Header("Build debug")]
        [SerializeField] private GameObject currentPropSelected;

        [SerializeField] private BuildPreview currentPreview;

        [SerializeField] private bool isEditMode;

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
            this.SwitchToFreeMode();
        }

        void Update() {
            this.ManageWorldTransparency();

            if (this.currentMode == CameraModeEnum.FREE) {
                this.ManageInteraction();
            } else if (this.currentMode == CameraModeEnum.BUILD) {
                this.ManageBuildMode();
            }

            if (Input.GetMouseButtonDown(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 300f;
            }

            if (Input.GetMouseButtonUp(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }


            // todo to remove
            if (Input.GetKeyDown(KeyCode.B)) {
                this.SwitchToBuildMode();
            } else if (Input.GetKeyDown(KeyCode.F)) {
                this.SwitchToFreeMode();
            }
        }

        public void ManageWorldTransparency() {
            if (!RoomManager.LocalPlayer) {
                return;
            }
            
            Vector3 dir = -(this.camera.transform.position - RoomManager.LocalPlayer.transform.position);
            if (Physics.Raycast(this.camera.transform.position, dir, out hit, 100, (1 << 12))) {
                if (hit.collider.gameObject != this.currentNearWall) {
                    if (this.currentNearWall) {
                        this.SetRendererVisible(this.currentNearWall, true);
                    }

                    this.currentNearWall = hit.collider.gameObject;
                    this.SetRendererVisible(this.currentNearWall, false);
                }
            } else {
                if (this.currentNearWall) {
                    this.SetRendererVisible(this.currentNearWall, true);
                    this.currentNearWall = null;
                }
            }
        }

        private void SetRendererVisible(GameObject target, bool state) {
            Collider[] neighbourWalls = Physics.OverlapSphere(target.transform.position, 2f, layerMaskTransparent);
            
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>()) {
                renderer.enabled = state;
            }

            foreach (Collider neighbourWall in neighbourWalls) {
                foreach (Renderer renderer in neighbourWall.GetComponentsInChildren<Renderer>()) {
                    renderer.enabled = state;
                }
            }
        }

        public void SwitchToBuildMode() {
            this.currentMode = CameraModeEnum.BUILD;
            this.freelookCamera.enabled = false;
        }

        public void SwitchToFreeMode() {
            this.currentMode = CameraModeEnum.FREE;
            this.freelookCamera.enabled = true;
            this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
        }

        private void ManageBuildMode() {
            // Manage camera movement
            if (Input.GetMouseButton(1)) { // Rotation
                this.transform.Rotate(new Vector3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0) * this.cameraRotationSpeed);
                this.transform.eulerAngles = new Vector3(this.transform.rotation.eulerAngles.x, this.transform.rotation.eulerAngles.y, 0);
            }

            if (Input.GetMouseButton(2)) { // Movement
                this.transform.Translate(new Vector3(-Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y"), 0) * this.cameraMoveSpeed);
            }

            float mouseScrollValue = Input.GetAxis("Mouse ScrollWheel");

            if (mouseScrollValue != 0f) { // Zoom
                this.transform.Translate(Vector3.forward * mouseScrollValue * this.cameraZoomSpeed);
            }

            // todo move that later
            if (Input.GetKeyDown(KeyCode.A) && !this.currentPropSelected) {
                this.SetCurrentSelectedProps(Instantiate(this.propsToInstantiate));
            }

            // Manage detection on ground and props
            int layerMask = this.currentPropSelected ? (1 << 9) : (1 << 10);

            if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                if (this.currentPropSelected) {
                    if (Input.GetKey(KeyCode.LeftShift)) {
                        if (Input.GetKeyDown(KeyCode.LeftArrow)) {
                            Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                            float newAngle = (Mathf.CeilToInt(currentLocalAngle.y / 90f) * 90f) - 90f;
                            this.currentPropSelected.transform.localEulerAngles = new Vector3(currentLocalAngle.x, newAngle, currentLocalAngle.z);
                        } else if (Input.GetKeyDown(KeyCode.RightArrow)) {
                            Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                            float newAngle = (Mathf.FloorToInt(currentLocalAngle.y / 90f) * 90f) + 90f;
                            this.currentPropSelected.transform.localEulerAngles = new Vector3(currentLocalAngle.x, newAngle, currentLocalAngle.z);
                        }
                    } else {
                        if (Input.GetKey(KeyCode.LeftArrow)) {
                            this.currentPropSelected.transform.Rotate(Vector3.forward * -this.propsRotationSpeed);
                        } else if (Input.GetKey(KeyCode.RightArrow)) {
                            this.currentPropSelected.transform.Rotate(Vector3.forward * this.propsRotationSpeed);
                        }
                    }

                    if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground")) {
                        float x = Mathf.FloorToInt(hit.point.x / this.propsStepSize) * this.propsStepSize;
                        float z = Mathf.FloorToInt(hit.point.z / this.propsStepSize) * this.propsStepSize;
                        this.currentPropSelected.transform.position = new Vector3(x, hit.point.y, z);
                    }

                    if (Input.GetMouseButtonDown(0) && this.currentPreview.IsPlaceable()) {
                        if (this.isEditMode) {
                            this.currentPropSelected.GetComponent<Props>().UpdateTransform();
                            this.currentPreview.Destroy();
                            this.currentPropSelected = null;
                        } else {
                            PhotonNetwork.InstantiateSceneObject("Prefabs/Props/" + this.propsToInstantiate.name, this.currentPropSelected.transform.position, this.currentPropSelected.transform.rotation);
                            Destroy(this.currentPropSelected);
                        }
                    }
                } else if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Props")) {
                    if (Input.GetMouseButtonDown(0)) {
                        this.SetCurrentSelectedProps(hit.collider.gameObject, true);
                    }
                }
            }
        }

        private void ManageInteraction() {
            if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, this.layerMaskInFreeMode)) {
                Interactable objectToInteract = null;

                if (hit.collider.CompareTag("Interactable")) {
                    Interactable interactable = hit.collider.GetComponentInParent<Interactable>();

                    if (RoomManager.LocalPlayer && interactable.CanInteract(RoomManager.LocalPlayer.transform.position)) {
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

        private void SetCurrentSelectedProps(GameObject props, bool isEditMode = false) {
            this.currentPropSelected = props;
            this.currentPreview = this.currentPropSelected.AddComponent<BuildPreview>();
            this.currentPreview.SetErrorMaterial(this.errorMaterial);
            this.isEditMode = isEditMode;
        }

        public void SetCameraTarget(Transform transform) {
            this.virtualCameraFollow.SetTarget(transform);
        }
    }
}