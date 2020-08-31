﻿using System;
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
using Sim.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private CinemachineFreeLook freelookCamera;

        [SerializeField] private VirtualCameraFollow virtualCameraFollow;
        [SerializeField] private LayerMask layerMaskInFreeMode;

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

        [Header("Build debug")]
        [SerializeField] private BuildModeEnum buildMode;
        
        private PropsConfig propsToInstantiate;

        private Props currentPropSelected;

        [SerializeField] private Package currentOpenedPackage;
        [SerializeField] private PropsConfig propsToPackage;

        [SerializeField] private PaintBucket currentOpenedBucket;
        [SerializeField] private PaintConfig paintToPackage;

        [SerializeField] private BuildPreview currentPreview;
        [SerializeField] private bool followMouse;

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

            this.buildMode = BuildModeEnum.NONE;
            this.camera = GetComponent<Camera>();
        }

        private void Start() {
            AdminPanelUI.OnPropsClicked += InstantiatePropsFromAdminPanel;
            AdminPanelUI.OnPaintClicked += InstantiatePaintFromAdminPanel;
            Package.OnOpened += OpenPackage;
            PaintBucket.OnOpened += OpenBucket;
            BuildPreviewPanelUI.OnValidate += ApplyBuildModification;
            BuildPreviewPanelUI.OnCanceled += ClearBuilds;
            BuildPreviewPanelUI.OnToggleHideProps += TogglePropsVisible;
            BuildPreviewPanelUI.OnToggleHideWalls += ToggleWallVisible;
        }

        private void OnDestroy() {
            AdminPanelUI.OnPropsClicked -= InstantiatePropsFromAdminPanel;
            AdminPanelUI.OnPaintClicked -= InstantiatePaintFromAdminPanel;
            Package.OnOpened -= OpenPackage;
            PaintBucket.OnOpened -= OpenBucket;
            BuildPreviewPanelUI.OnValidate -= ApplyBuildModification;
            BuildPreviewPanelUI.OnCanceled -= ClearBuilds;
            BuildPreviewPanelUI.OnToggleHideProps -= TogglePropsVisible;
            BuildPreviewPanelUI.OnToggleHideWalls -= ToggleWallVisible;
        }

        void Update() {
            this.ManageWorldTransparency();

            if (Input.GetKeyDown(KeyCode.F)) {
                if (this.currentMode == CameraModeEnum.FREE) {
                    HUDManager.Instance.DisplayAdminPanel(true);
                } else if (this.currentMode == CameraModeEnum.BUILD) {
                    HUDManager.Instance.DisplayAdminPanel(false);
                }
            }

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
            if (!RoomManager.LocalPlayer || this.currentOpenedBucket) {
                return;
            }

            Vector3 dir = -(this.camera.transform.position - (this.currentPropSelected ? this.currentPropSelected.transform.position : RoomManager.LocalPlayer.transform.position));
            if (Physics.Raycast(this.camera.transform.position, dir, out hit, 20, (1 << 10 | 1 << 12))) {
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

        public void TogglePropsVisible(bool hide) {
            FindObjectsOfType<Props>().ToList().Where(x => x.GetType() != typeof(Wall)).Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(foundationRenderer => {
                if (foundationRenderer) {
                    foundationRenderer.SetVisibilityMode(hide ? FoundationVisibilityEnum.FORCE_HIDE : FoundationVisibilityEnum.AUTO);
                }
            });
        }

        public void ToggleWallVisible(bool hide) {
            FindObjectsOfType<Wall>().ToList().Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(foundationRenderer => {
                if (foundationRenderer) {
                    foundationRenderer.SetVisibilityMode(hide ? FoundationVisibilityEnum.FORCE_HIDE : FoundationVisibilityEnum.AUTO);
                }
            });
        }


        private void ShowWallFoundation(GameObject target, bool state) {
            List<FoundationRenderer> objectsToHide = Physics.OverlapSphere(target.transform.position, 2.5f).ToList().Select(x => x.GetComponentInParent<FoundationRenderer>()).ToList();
            objectsToHide.Add(target.GetComponentInParent<FoundationRenderer>());
            objectsToHide.ForEach(foundationRenderer => {
                if (foundationRenderer && foundationRenderer.CanInteractWithCameraDistance()) { // prevent NPE due to get componentInParent
                    foundationRenderer.ShowFoundation(state);
                }
            });
        }

        private void SwitchToBuildMode() {
            this.currentMode = CameraModeEnum.BUILD;
            this.freelookCamera.enabled = false;
        }

        private void SwitchToFreeMode() {
            this.currentMode = CameraModeEnum.FREE;
            this.virtualCameraFollow.SetTarget(RoomManager.LocalPlayer.GetHeadTargetForCamera());
            this.freelookCamera.enabled = true;
            this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
        }

        private void ManageInteraction() {
            if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, this.layerMaskInFreeMode)) {
                Props objectToInteract = hit.collider.GetComponentInParent<Props>();
                bool canInteract = objectToInteract && RoomManager.LocalPlayer && RoomManager.LocalPlayer.CanInteractWith(objectToInteract);

                if (Input.GetMouseButtonDown(0)) {
                    if (canInteract) {
                        HUDManager.Instance.DisplayContextMenu(true, Input.mousePosition, objectToInteract);
                    } else {
                        RoomManager.Instance.MovePlayerTo(hit.point);
                        HUDManager.Instance.DisplayContextMenu(false, Vector3.zero);
                    }
                }
            }
        }

        public void SetCameraTarget(Transform transform) {
            this.virtualCameraFollow.SetTarget(transform);
        }

        #region Build Management

        public BuildModeEnum GetBuildMode() {
            return this.buildMode;
        }

        private void OpenPackage(Package package) {
            this.currentOpenedPackage = package;
            this.InstantiateProps(package.GetPropsInside());
            this.buildMode = BuildModeEnum.UNPACKAGING;
        }

        private void OpenBucket(PaintBucket bucket) {
            this.ClearBuilds();

            this.currentOpenedBucket = bucket;

            this.buildMode = BuildModeEnum.PAINT;
            
            // todo sortir ça
            HUDManager.Instance.DisplayAdminPanel(false);
            HUDManager.Instance.DisplayBuildPreviewPanel(true);
            
            this.SwitchToBuildMode();
            
            FindObjectsOfType<Props>().ToList().Where(x => x.GetType() == typeof(Wall)).Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(foundationRenderer => {
                if (foundationRenderer) {
                    foundationRenderer.SetVisibilityMode(FoundationVisibilityEnum.FORCE_SHOW);
                }
            });
        }

        private void InstantiatePropsFromAdminPanel(PropsConfig propsConfig) {
            this.propsToPackage = propsConfig;
            InstantiateProps(this.propsToPackage.GetPackage().GetConfiguration());
            this.buildMode = BuildModeEnum.PACKAGING;
        }

        private void InstantiatePaintFromAdminPanel(PaintConfig paintConfig) {
            this.paintToPackage = paintConfig;
            InstantiateProps(this.paintToPackage.GetBucketPrefab().GetConfiguration());
            this.buildMode = BuildModeEnum.PACKAGING;
        }

        private void SetCurrentSelectedProps(PropsConfig propsConfig, bool isEditMode = false) {
            Props props = Instantiate(propsConfig.GetPrefab());
            props.SetConfiguration(propsConfig);
            
            this.isEditMode = isEditMode;
            this.initialPosition = props.transform.position;
            this.initialRotation = props.transform.rotation;

            this.currentPropSelected = props;
            this.currentPreview = this.currentPropSelected.gameObject.AddComponent<BuildPreview>();
            this.currentPreview.SetErrorMaterial(DatabaseManager.Instance.GetErrorMaterial());

            // todo sortir ça
            HUDManager.Instance.DisplayAdminPanel(false);
            HUDManager.Instance.DisplayBuildPreviewPanel(true);

            this.SwitchToBuildMode();

            this.followMouse = true;
        }

        private void InstantiateProps(PropsConfig config) {
            this.ClearBuilds();

            this.propsToInstantiate = config;

            this.SetCurrentSelectedProps(config);
        }

        /**
         * Used to clear build mode if there was a current prop selected
         */
        public void ClearBuilds() {
            if (!this.currentPropSelected && !this.currentOpenedBucket) {
                return;
            }

            if (this.isEditMode) {
                this.currentPropSelected.transform.position = this.initialPosition;
                this.currentPropSelected.transform.rotation = this.initialRotation;
                this.currentPreview.Destroy();
                this.currentPropSelected = null;
            } else if (this.currentPropSelected) {
                Destroy(this.currentPropSelected.gameObject);
            }

            if (this.currentOpenedPackage) { // if a package was opened reset it
                this.currentOpenedPackage = null;
            }

            if (this.currentOpenedBucket) { // if a bucket was opened reset it and all walls in preview
                FindObjectsOfType<Props>().ToList().Where(x => x.GetType() == typeof(Wall)).Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(foundationRenderer => {
                    if (foundationRenderer) {
                        foundationRenderer.SetVisibilityMode(FoundationVisibilityEnum.AUTO);
                    }
                });
                FindObjectsOfType<Wall>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.Reset());
                this.currentOpenedBucket = null;
            }

            this.SwitchToFreeMode();
            
            this.buildMode = BuildModeEnum.NONE;

            HUDManager.Instance.DisplayBuildPreviewPanel(false);
        }

        public void ApplyBuildModification() {
            if ((!this.currentPropSelected || !this.currentPreview.IsPlaceable()) && !this.currentOpenedBucket) { // prevent to pose if something goes wrong with UI
                return;
            }

            if (this.currentPropSelected) {
                if (this.isEditMode) {
                    this.currentPropSelected.UpdateTransform();
                    this.currentPreview.Destroy();
                    this.currentPropSelected = null;
                } else {
                    GameObject propsInstanciated = PhotonNetwork.InstantiateSceneObject(CommonUtils.GetRelativePathFromResources(this.propsToInstantiate.GetPrefab()), this.currentPropSelected.transform.position, this.currentPropSelected.transform.rotation);
                    Props props = propsInstanciated.GetComponent<Props>();
                    props.SetConfiguration(this.propsToInstantiate);
                    
                    // Manage packaging for props
                    if (this.propsToPackage) {
                        props.SetIsBuilt(true);
                        propsInstanciated.GetComponent<Package>().SetPropsInside(this.propsToPackage.GetId());
                        this.propsToPackage = null;
                    }

                    // Manage packaging for paint
                    if (this.paintToPackage) {
                        props.SetIsBuilt(true);
                        propsInstanciated.GetComponent<PaintBucket>().SetPaintConfigId(this.paintToPackage.GetId());
                        this.paintToPackage = null;
                    }

                    // Manage unpackaging
                    if (this.currentOpenedPackage) { // if props come from a package so remove package
                        props.SetIsBuilt(false);
                        PhotonNetwork.Destroy(this.currentOpenedPackage.gameObject);
                    }

                    Destroy(this.currentPropSelected.gameObject);
                    this.propsToInstantiate = null;
                }
            } else if (this.currentOpenedBucket) {
                FindObjectsOfType<Props>().ToList().Where(x => x.GetType() == typeof(Wall)).Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(foundationRenderer => {
                    if (foundationRenderer) {
                        foundationRenderer.SetVisibilityMode(FoundationVisibilityEnum.AUTO);
                    }
                });
                FindObjectsOfType<Wall>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.ApplyModification());
                this.currentOpenedBucket = null;
            }

            this.SwitchToFreeMode();
            
            this.buildMode = BuildModeEnum.NONE;

            HUDManager.Instance.DisplayBuildPreviewPanel(false);
        }

        private void ManageBuildMode() {
            if (!this.currentPropSelected && !this.currentOpenedBucket) {
                return;
            }

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

            if (this.currentPropSelected) {
                // manage rotation of current props
                if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND) {
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
                }

                // Manage surface detection
                int layerMask = (1 << 11); // If current props is not following mouse so raycast only preview items

                if (followMouse) {
                    if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND) {
                        layerMask = (1 << 9);
                    } else if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.WALL) {
                        layerMask = (1 << 12);
                    }
                }

                if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                    // manage position to move current props
                    if (this.followMouse) {
                        if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND && hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground")) {
                            float x = Mathf.FloorToInt(hit.point.x / this.propsStepSize) * this.propsStepSize;
                            float z = Mathf.FloorToInt(hit.point.z / this.propsStepSize) * this.propsStepSize;
                            this.currentPropSelected.transform.position = new Vector3(x, hit.point.y + (hit.normal.y * 0.01f), z);
                        }

                        if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.WALL && hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall")) {
                            this.currentPropSelected.transform.position = hit.point + (hit.normal * 0.01f);
                            Vector3 rotation = this.currentPropSelected.transform.localEulerAngles;

                            if (hit.normal == Vector3.forward) {
                                rotation.y = 180;
                            } else if (hit.normal == -Vector3.forward) {
                                rotation.y = 0f;
                            } else if (hit.normal == -Vector3.left) {
                                rotation.y = 270;
                            } else if (hit.normal == Vector3.left) {
                                rotation.y = 90f;
                            }

                            this.currentPropSelected.transform.localEulerAngles = rotation;
                        }
                    }

                    // manage props follow value
                    if (Input.GetMouseButtonDown(0)) {
                        if (followMouse && this.currentPreview.IsPlaceable()) {
                            this.followMouse = false;

                            this.virtualCameraFollow.SetTarget(this.currentPreview.transform);
                            this.freelookCamera.enabled = true;
                            this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
                        } else if (!followMouse) {
                            this.followMouse = true;
                            this.freelookCamera.enabled = false;
                        }
                    }
                }
            } else if (this.currentOpenedBucket) {
                // Manage surface detection
                int layerMask = -1;

                if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.GROUND) {
                    layerMask = (1 << 9);
                } else if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.WALL) {
                    layerMask = (1 << 12);
                }

                if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                    if (layerMask == (1 << 12) && Input.GetMouseButtonDown(0)) {
                        Wall wall = hit.collider.GetComponent<Wall>();
                        wall.PreviewMaterialOnFace(hit, this.currentOpenedBucket);
                    }
                }
            }

        }

        #endregion
    }
}