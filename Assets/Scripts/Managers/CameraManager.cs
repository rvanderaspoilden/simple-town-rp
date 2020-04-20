using System;
using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using Photon.Pun;
using Photon.Pun.Demo.PunBasics;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using Sim.Scriptables;
using Sim.UI;
using UnityEngine;
using UnityEngine.EventSystems;

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

        [SerializeField] private Material errorMaterial;

        [Header("Build debug")]
        [SerializeField] private PropsConfig propsToInstantiate;
        [SerializeField] private Props currentPropSelected;

        [SerializeField] private BuildPreview currentPreview;

        [SerializeField] private bool isEditMode;
        [SerializeField] private Vector3 initialPosition; // used to rollback
        [SerializeField] private Quaternion initialRotation; // used to rollback

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
            CatalogUI.OnPropsClicked += InstantiateProps;
        }

        private void OnDestroy() {
            CatalogUI.OnPropsClicked -= InstantiateProps;
        }

        void Update() {
            this.ManageWorldTransparency();

            if (this.currentMode == CameraModeEnum.FREE && !EventSystem.current.IsPointerOverGameObject()) {
                this.ManageInteraction();
            } else if (this.currentMode == CameraModeEnum.BUILD && !EventSystem.current.IsPointerOverGameObject()) {
                this.ManageBuildMode();
            }

            if (Input.GetMouseButtonDown(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 300f;
            }

            if (Input.GetMouseButtonUp(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }
        }

        public CameraModeEnum GetCurrentMode() {
            return this.currentMode;
        }

        public void ManageWorldTransparency() {
            if (!RoomManager.LocalPlayer) {
                return;
            }

            Vector3 dir = -(this.camera.transform.position - RoomManager.LocalPlayer.transform.position);
            if (Physics.Raycast(this.camera.transform.position, dir, out hit, 100, (1 << 12))) {
                if (hit.collider.gameObject != this.currentNearWall) {
                    if (this.currentNearWall) {
                        this.ShowWallFoundation(this.currentNearWall, false);
                    }

                    this.currentNearWall = hit.collider.gameObject;
                    this.ShowWallFoundation(this.currentNearWall, true);
                }
            } else {
                if (this.currentNearWall) {
                    this.ShowWallFoundation(this.currentNearWall, false);
                    this.currentNearWall = null;
                }
            }
        }

        private void ShowWallFoundation(GameObject target, bool state) {
            List<FoundationRenderer> objectsToHide = Physics.OverlapSphere(target.transform.position, 2.5f, layerMaskTransparent).ToList().Select(x => x.GetComponentInParent<FoundationRenderer>()).ToList();
            objectsToHide.Add(target.GetComponentInParent<FoundationRenderer>());
            objectsToHide.ForEach(x => {
                if (x) { // prevent NPE due to get componentInParent
                    x.ShowFoundation(state);
                }
            });
        }

        public void ToggleMode() {
            if (this.currentMode == CameraModeEnum.FREE) {
                this.SwitchToBuildMode();
            } else {
                this.ClearBuilds();

                this.SwitchToFreeMode();
            }
        }

        private void SwitchToBuildMode() {
            this.currentMode = CameraModeEnum.BUILD;
            this.freelookCamera.enabled = false;
        }

        private void SwitchToFreeMode() {
            this.currentMode = CameraModeEnum.FREE;
            this.freelookCamera.enabled = true;
            this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
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

        public void SetCameraTarget(Transform transform) {
            this.virtualCameraFollow.SetTarget(transform);
        }

        #region Build Management

        private void SetCurrentSelectedProps(Props props, bool isEditMode = false) {
            this.isEditMode = isEditMode;
            this.initialPosition = props.transform.position;
            this.initialRotation = props.transform.rotation;

            this.currentPropSelected = props;
            this.currentPreview = this.currentPropSelected.gameObject.AddComponent<BuildPreview>();
            this.currentPreview.SetErrorMaterial(this.errorMaterial);
        }

        private void InstantiateProps(PropsConfig config) {
            if (this.currentPropSelected) { // reset current selected props
                this.ClearBuilds();
            }

            this.propsToInstantiate = config;
            
            this.SetCurrentSelectedProps(Instantiate(this.propsToInstantiate.GetPrefab()));
        }


        /**
         * Used to clear build mode if there was a current prop selected
         */
        private void ClearBuilds() {
            if (!this.currentPropSelected) {
                return;
            }

            if (this.isEditMode) {
                this.currentPropSelected.transform.position = this.initialPosition;
                this.currentPropSelected.transform.rotation = this.initialRotation;
                this.currentPreview.Destroy();
                this.currentPropSelected = null;
            } else {
                Destroy(this.currentPropSelected.gameObject);
            }
        }

        private void ApplyBuildModification() {
            if (!this.currentPropSelected) {
                return;
            }

            if (this.isEditMode) {
                this.currentPropSelected.UpdateTransform();
                this.currentPreview.Destroy();
                this.currentPropSelected = null;
            } else {
                PhotonNetwork.InstantiateSceneObject("Prefabs/Props/" + this.propsToInstantiate.GetPrefab().name, this.currentPropSelected.transform.position, this.currentPropSelected.transform.rotation);
                Destroy(this.currentPropSelected.gameObject);
                this.propsToInstantiate = null;
            }
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

            // Manage surface detection
            int layerMask = (1 << 10); // Props if there isn't current prop selected

            if (this.currentPropSelected) {
                if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND) {
                    layerMask = (1 << 9);
                } else if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.WALL) {
                    layerMask = (1 << 12);
                }
            }
            

            if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                if (this.currentPropSelected) {
                    // manage rotation of current props
                    if (Input.GetKeyDown(KeyCode.DownArrow)) {
                        Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                        float newAngle = (Mathf.CeilToInt(currentLocalAngle.y / 90f) * 90f) - 90f;
                        this.currentPropSelected.transform.localEulerAngles = new Vector3(currentLocalAngle.x, newAngle, currentLocalAngle.z);
                    } else if (Input.GetKeyDown(KeyCode.UpArrow)) {
                        Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                        float newAngle = (Mathf.FloorToInt(currentLocalAngle.y / 90f) * 90f) + 90f;
                        this.currentPropSelected.transform.localEulerAngles = new Vector3(currentLocalAngle.x, newAngle, currentLocalAngle.z);
                    } else if (Input.GetKey(KeyCode.LeftArrow)) {
                        this.currentPropSelected.transform.Rotate(Vector3.forward * -this.propsRotationSpeed);
                    } else if (Input.GetKey(KeyCode.RightArrow)) {
                        this.currentPropSelected.transform.Rotate(Vector3.forward * this.propsRotationSpeed);
                    }

                    // manage position to move current props
                    if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND && hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground")) {
                        float x = Mathf.FloorToInt(hit.point.x / this.propsStepSize) * this.propsStepSize;
                        float z = Mathf.FloorToInt(hit.point.z / this.propsStepSize) * this.propsStepSize;
                        this.currentPropSelected.transform.position = new Vector3(x, hit.point.y + (hit.normal.y * 0.01f), z);
                    }
                    
                    if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.WALL && hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall")) {
                        this.currentPropSelected.transform.position = hit.point + (hit.normal * 0.01f);
                        this.currentPropSelected.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    }

                    // manage validation of props posing
                    if (Input.GetMouseButtonDown(0) && this.currentPreview.IsPlaceable()) {
                        this.ApplyBuildModification();
                    }
                } else if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Props")) {
                    if (Input.GetMouseButtonDown(0)) {
                        this.SetCurrentSelectedProps(hit.collider.GetComponentInParent<Props>(), true);
                    }
                }
            }
        }

        #endregion
    }
}