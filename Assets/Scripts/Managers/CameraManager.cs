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
using Sim.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.UI;
using UnityEngine.XR;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")] [SerializeField] private CinemachineFreeLook freelookCamera;

        [SerializeField] private VirtualCameraFollow virtualCameraFollow;
        [SerializeField] private LayerMask layerMaskInFreeMode;

        [Header("Only for debug")] [SerializeField]
        private new Camera camera;

        [SerializeField] private CameraModeEnum currentMode;
        [SerializeField] private List<FoundationRenderer> displayedFoundationRenderers;

        [Header("Build settings")] [SerializeField]
        private float cameraRotationSpeed = 3f;

        [SerializeField] private float cameraMoveSpeed = 3f;
        [SerializeField] private float cameraZoomSpeed = 3f;

        [SerializeField] private float propsRotationSpeed = 1.5f;
        [SerializeField] private float propsStepSize = 0.1f;

        [Header("Build debug")] [SerializeField]
        private BuildModeEnum buildMode;

        private PropsConfig propsConfigToInstantiate;

        private Props currentPropSelected;

        private float lastCameraPosition;

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
            this.displayedFoundationRenderers = new List<FoundationRenderer>();
        }

        private void Start() {
            AdminPanelUI.OnPropsClicked += OnSelectPropsFromAdminPanel;
            AdminPanelUI.OnPaintClicked += OnSelectPaintFromAdminPanel;
            Package.OnOpened += OpenPackage;
            PaintBucket.OnOpened += OpenBucket;
            BuildPreviewPanelUI.OnValidate += ApplyBuildModification;
            BuildPreviewPanelUI.OnCanceled += OnCancelBuildPreview;
            BuildPreviewPanelUI.OnToggleHideProps += TogglePropsVisible;
            BuildPreviewPanelUI.OnToggleHideWalls += ToggleWallVisible;
        }

        private void OnDestroy() {
            AdminPanelUI.OnPropsClicked -= OnSelectPropsFromAdminPanel;
            AdminPanelUI.OnPaintClicked -= OnSelectPaintFromAdminPanel;
            Package.OnOpened -= OpenPackage;
            PaintBucket.OnOpened -= OpenBucket;
            BuildPreviewPanelUI.OnValidate -= ApplyBuildModification;
            BuildPreviewPanelUI.OnCanceled -= OnCancelBuildPreview;
            BuildPreviewPanelUI.OnToggleHideProps -= TogglePropsVisible;
            BuildPreviewPanelUI.OnToggleHideWalls -= ToggleWallVisible;
        }

        void Update() {
            this.ManageWorldTransparency();

            if (Input.GetKeyDown(KeyCode.F) && PhotonNetwork.IsMasterClient && AppartmentManager.instance &&
                AppartmentManager.instance.IsOwner(NetworkManager.Instance.Personnage)) {
                if (this.currentMode == CameraModeEnum.FREE) {
                    HUDManager.Instance.DisplayAdminPanel(true);
                }
                else if (this.currentMode == CameraModeEnum.BUILD) {
                    HUDManager.Instance.DisplayAdminPanel(false);
                }
            }

            if (this.currentMode == CameraModeEnum.FREE && !EventSystem.current.IsPointerOverGameObject()) {
                this.ManageInteraction();
            }
            else if (this.currentMode == CameraModeEnum.BUILD && !EventSystem.current.IsPointerOverGameObject()) {
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
            if (!RoomManager.LocalPlayer || this.currentOpenedBucket || (this.currentPropSelected && this.currentPropSelected.IsWallProps())) {
                return;
            }

            if (this.camera.transform.position.magnitude != this.lastCameraPosition) {
                this.lastCameraPosition = this.camera.transform.position.magnitude;

                Vector3 dir = -(this.camera.transform.position -
                                (this.currentPropSelected ? this.currentPropSelected.transform.position : RoomManager.LocalPlayer.transform.position));
                Debug.DrawRay(this.camera.transform.position, dir, Color.blue);

                RaycastHit[] hits = Physics.RaycastAll(this.camera.transform.position, dir, 20, (1 << 10 | 1 << 12));
                if (hits.Length > 0) {
                    List<FoundationRenderer> hiddenObject = new List<FoundationRenderer>();

                    foreach (RaycastHit wallHit in hits) {
                        hiddenObject.AddRange(this.HidePropsNear(wallHit.point));
                    }

                    this.HideFoundationForPropsNotIn(hiddenObject);
                }
                else if (this.displayedFoundationRenderers.Count > 0) // If camera doesn't hit wall so reset all hidden props
                {
                    this.displayedFoundationRenderers.ForEach(foundationRenderer => foundationRenderer.ShowFoundation(false));
                    this.displayedFoundationRenderers.Clear();
                }
            }
        }

        public void TogglePropsVisible(bool hide) {
            FindObjectsOfType<Props>().ToList().Where(x => x.GetType() != typeof(Wall)).Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(
                foundationRenderer => {
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

        private List<FoundationRenderer> HidePropsNear(Vector3 pos) {
            List<FoundationRenderer> objectsToHide = Physics.OverlapSphere(pos, 1.2f)
                .ToList()
                .Select(x => x.GetComponentInParent<FoundationRenderer>())
                .ToList();

            objectsToHide.ForEach(foundationRenderer => {
                if (foundationRenderer && foundationRenderer.CanInteractWithCameraDistance()) {
                    // prevent NPE due to get componentInParent
                    foundationRenderer.ShowFoundation(true);

                    this.displayedFoundationRenderers.Add(foundationRenderer);
                }
            });

            return objectsToHide;
        }

        private void HideFoundationForPropsNotIn(List<FoundationRenderer> objectHidden) {
            // Reset objects which aren't in view area
            IEnumerable<FoundationRenderer> difference = this.displayedFoundationRenderers.Except(objectHidden);

            foreach (FoundationRenderer foundationRenderer in difference) {
                foundationRenderer.ShowFoundation(false);
            }
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
                    }
                    else {
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
            this.CleanBuildPreview();

            this.currentOpenedBucket = bucket;

            this.buildMode = BuildModeEnum.PAINT;

            // todo sortir ça
            HUDManager.Instance.DisplayAdminPanel(false);
            HUDManager.Instance.DisplayBuildPreviewPanel(true);

            this.SwitchToBuildMode();

            if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.WALL) {
                FindObjectsOfType<Props>().ToList().Where(x => x.GetType() == typeof(Wall)).Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(
                    foundationRenderer => {
                        if (foundationRenderer) {
                            foundationRenderer.SetVisibilityMode(FoundationVisibilityEnum.FORCE_SHOW);
                        }
                    });
            }
        }

        /**
         * Called when props was chosen from admin panel
         */
        private void OnSelectPropsFromAdminPanel(PropsConfig propsConfig) {
            this.propsToPackage = propsConfig;

            InstantiateProps(this.propsToPackage.GetPackageConfig());
            this.buildMode = BuildModeEnum.PACKAGING;
        }

        /**
         * Called when paint was chosen from admin panel
         */
        private void OnSelectPaintFromAdminPanel(PaintConfig paintConfig) {
            this.paintToPackage = paintConfig;
            InstantiateProps(this.paintToPackage.GetBucketPropsConfig());
            this.buildMode = BuildModeEnum.PACKAGING;
        }

        private void SetCurrentSelectedProps(Props props) {
            // Used for edit mode (useless actually)
            this.initialPosition = props.transform.position;
            this.initialRotation = props.transform.rotation;

            this.currentPropSelected = props;
            this.currentPreview = this.currentPropSelected.gameObject.AddComponent<BuildPreview>();
            this.currentPreview.SetErrorMaterial(DatabaseManager.Instance.GetErrorMaterial());
        }

        private void OnCancelBuildPreview() {
            // Clean all state
            this.CleanBuildPreview();

            // Then come back to default view
            this.SwitchToFreeMode();

            // Reset build mode to be safe for next time
            this.buildMode = BuildModeEnum.NONE;

            // Hide build preview panel
            HUDManager.Instance.DisplayBuildPreviewPanel(false);
        }

        private void InstantiateProps(PropsConfig config) {
            // Clean all previous states
            this.CleanBuildPreview();

            this.propsConfigToInstantiate = config;

            Props props = PropsManager.instance.InstantiateProps(config, false);

            this.SetCurrentSelectedProps(props);

            // todo sortir ça
            HUDManager.Instance.DisplayAdminPanel(false);
            HUDManager.Instance.DisplayBuildPreviewPanel(true);

            if (this.currentMode != CameraModeEnum.BUILD) {
                this.SwitchToBuildMode();
            }

            this.followMouse = true;
        }

        /**
         * Used to clear build mode if there was a current prop selected
         */
        public void CleanBuildPreview() {
            if (!this.currentPropSelected && !this.currentOpenedBucket) {
                return;
            }

            // Reactivate when edit mode
            /**if (this.isEditMode) { // Reset current props selected to his initial state
                this.currentPropSelected.transform.position = this.initialPosition;
                this.currentPropSelected.transform.rotation = this.initialRotation;
                this.currentPreview.Destroy();
                this.currentPropSelected = null;
            } else if (this.currentPropSelected) {
                Destroy(this.currentPropSelected.gameObject);
            } **/

            if (this.currentPropSelected) {
                // if props is selected => destroy it
                Destroy(this.currentPropSelected.gameObject);
            }

            if (this.currentOpenedPackage) {
                // if a package was opened reset it
                this.currentOpenedPackage = null;
            }

            if (this.currentOpenedBucket) {
                // if a bucket was opened reset it and all walls in preview
                if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.WALL) {
                    FindObjectsOfType<Props>().ToList().Where(x => x.GetType() == typeof(Wall)).Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(
                        foundationRenderer => {
                            if (foundationRenderer) {
                                foundationRenderer.SetVisibilityMode(FoundationVisibilityEnum.AUTO);
                            }
                        });
                    FindObjectsOfType<Wall>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.Reset());
                }
                else if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.GROUND) {
                    FindObjectsOfType<Ground>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.ResetPreview());
                }

                this.currentOpenedBucket = null;
            }
        }

        public void ApplyBuildModification() {
            if ((!this.currentPropSelected || !this.currentPreview.IsPlaceable()) && !this.currentOpenedBucket) {
                // prevent to pose if something goes wrong with UI
                return;
            }

            if (this.currentPropSelected) {
                if (this.isEditMode) {
                    this.currentPropSelected.UpdateTransform();
                    this.currentPreview.Destroy();
                    this.currentPropSelected = null;
                }
                else {
                    Props props = PropsManager.instance.InstantiateProps(this.propsConfigToInstantiate, this.currentPropSelected.transform.position,
                        this.currentPropSelected.transform.rotation, true);

                    // Manage packaging for props
                    if (this.propsToPackage) {
                        props.SetIsBuilt(true);
                        props.GetComponent<Package>().SetPropsInside(this.propsToPackage.GetId(), RpcTarget.All);
                        this.propsToPackage = null;
                    }

                    // Manage packaging for paint
                    if (this.paintToPackage) {
                        props.SetIsBuilt(true);
                        props.GetComponent<PaintBucket>().SetPaintConfigId(this.paintToPackage.GetId(), RpcTarget.All);
                        this.paintToPackage = null;
                    }

                    // Manage unpackaging
                    if (this.currentOpenedPackage) {
                        // if props come from a package so remove package
                        props.SetIsBuilt(!this.propsConfigToInstantiate.MustBeBuilt());
                        PhotonNetwork.Destroy(this.currentOpenedPackage.gameObject);
                    }

                    Destroy(this.currentPropSelected.gameObject);
                    this.propsConfigToInstantiate = null;
                }
            }
            else if (this.currentOpenedBucket) {
                if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.WALL) {
                    FindObjectsOfType<Props>().ToList().Where(x => x.GetType() == typeof(Wall)).Select(x => x.GetComponent<FoundationRenderer>()).ToList().ForEach(
                        foundationRenderer => {
                            if (foundationRenderer) {
                                foundationRenderer.SetVisibilityMode(FoundationVisibilityEnum.AUTO);
                            }
                        });
                    FindObjectsOfType<Wall>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.ApplyModification());
                }
                else if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.GROUND) {
                    FindObjectsOfType<Ground>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.ApplyModification());
                }

                this.currentOpenedBucket = null;
            }

            RoomManager.Instance.SaveRoom();

            this.SwitchToFreeMode();

            this.buildMode = BuildModeEnum.NONE;

            HUDManager.Instance.DisplayBuildPreviewPanel(false);
        }

        private void ManageBuildMode() {
            if (!this.currentPropSelected && !this.currentOpenedBucket) {
                return;
            }

            // Manage camera movement
            if (Input.GetMouseButton(1)) {
                // Rotation
                this.transform.Rotate(new Vector3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0) * this.cameraRotationSpeed);
                this.transform.eulerAngles = new Vector3(this.transform.rotation.eulerAngles.x, this.transform.rotation.eulerAngles.y, 0);
            }

            if (Input.GetMouseButton(2)) {
                // Movement
                this.transform.Translate(new Vector3(-Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y"), 0) * this.cameraMoveSpeed);
            }

            float mouseScrollValue = Input.GetAxis("Mouse ScrollWheel");

            if (mouseScrollValue != 0f) {
                // Zoom
                this.transform.Translate(Vector3.forward * mouseScrollValue * this.cameraZoomSpeed);
            }

            if (this.currentPropSelected) {
                // manage rotation of current props
                if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND) {
                    if (Input.GetKeyDown(KeyCode.DownArrow)) {
                        Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                        float newAngle = (Mathf.CeilToInt(currentLocalAngle.y / 90f) * 90f) - 90f;
                        this.currentPropSelected.transform.localEulerAngles = new Vector3(currentLocalAngle.x, newAngle, currentLocalAngle.z);
                    }
                    else if (Input.GetKeyDown(KeyCode.UpArrow)) {
                        Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                        float newAngle = (Mathf.FloorToInt(currentLocalAngle.y / 90f) * 90f) + 90f;
                        this.currentPropSelected.transform.localEulerAngles = new Vector3(currentLocalAngle.x, newAngle, currentLocalAngle.z);
                    }
                    else if (Input.GetKey(KeyCode.LeftArrow)) {
                        Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                        this.currentPropSelected.transform.localEulerAngles =
                            new Vector3(currentLocalAngle.x, currentLocalAngle.y - this.propsRotationSpeed, currentLocalAngle.z);
                    }
                    else if (Input.GetKey(KeyCode.RightArrow)) {
                        Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                        this.currentPropSelected.transform.localEulerAngles =
                            new Vector3(currentLocalAngle.x, currentLocalAngle.y + this.propsRotationSpeed, currentLocalAngle.z);
                    }
                }

                // Manage surface detection
                int layerMask = (1 << 11); // If current props is not following mouse so raycast only preview items

                if (followMouse) {
                    if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND) {
                        layerMask = (1 << 9);
                    }
                    else if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.WALL) {
                        layerMask = (1 << 12);
                    }
                }

                if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                    // manage position to move current props
                    if (this.followMouse) {
                        if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.GROUND &&
                            hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground")) {
                            float x = Mathf.FloorToInt(hit.point.x / this.propsStepSize) * this.propsStepSize;
                            float z = Mathf.FloorToInt(hit.point.z / this.propsStepSize) * this.propsStepSize;
                            this.currentPropSelected.transform.position = new Vector3(x, hit.point.y + (hit.normal.y * 0.01f), z);
                        }

                        if (this.currentPropSelected.GetConfiguration().GetSurfaceToPose() == BuildSurfaceEnum.WALL &&
                            hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall")) {
                            this.currentPropSelected.transform.position = hit.point + (hit.normal * 0.01f);
                            Vector3 rotation = this.currentPropSelected.transform.localEulerAngles;

                            if (hit.normal == Vector3.forward) {
                                rotation.y = 180;
                            }
                            else if (hit.normal == -Vector3.forward) {
                                rotation.y = 0f;
                            }
                            else if (hit.normal == -Vector3.left) {
                                rotation.y = 270;
                            }
                            else if (hit.normal == Vector3.left) {
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
                        }
                        else if (!followMouse) {
                            this.followMouse = true;
                            this.freelookCamera.enabled = false;
                        }
                    }
                }
            }
            else if (this.currentOpenedBucket) {
                // Manage surface detection
                int layerMask = -1;

                if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.GROUND) {
                    layerMask = (1 << 9);
                }
                else if (this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.WALL) {
                    layerMask = (1 << 12);
                }

                if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask) && Input.GetMouseButtonDown(0)) {
                    if (layerMask == (1 << 12)) {
                        Wall wall = hit.collider.GetComponent<Wall>();
                        wall.PreviewMaterialOnFace(hit, this.currentOpenedBucket);
                    }
                    else if (layerMask == (1 << 9)) {
                        Ground ground = hit.collider.GetComponent<Ground>();
                        ground.Preview(this.currentOpenedBucket.GetPaintConfig());
                    }
                }
            }
        }

        #endregion
    }
}