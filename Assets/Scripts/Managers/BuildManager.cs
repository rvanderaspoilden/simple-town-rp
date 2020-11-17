using System;
using System.Linq;
using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using Sim.UI;
using Sim.Utils;
using UnityEngine;

namespace Sim {
    public class BuildManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private new Camera camera;

        [SerializeField]
        private float propsRotationSpeed;

        [SerializeField]
        private float propsStepSize;

        [Header("Debug")]
        [SerializeField]
        private Props currentPropSelected;

        [SerializeField]
        private PaintBucket currentOpenedBucket;

        [SerializeField]
        private BuildModeEnum mode;

        [SerializeField]
        private BuildPreview currentPreview;

        private RaycastHit hit;

        public delegate void ValidatePropModification(PropsConfig propsConfig, Vector3 position, Quaternion rotation);

        public static event ValidatePropModification OnValidatePropModification;

        public delegate void ValidatePaintModification();

        public static event ValidatePaintModification OnValidatePaintModification;

        public delegate void CancelModification();
        
        public static event CancelModification OnCancel;

        public static BuildManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this);
            }

            Instance = this;
        }

        private void Start() {
            BuildPreviewPanelUI.OnValidate += Apply;
            BuildPreviewPanelUI.OnCanceled += Cancel;
        }

        private void OnDestroy() {
            BuildPreviewPanelUI.OnValidate -= Apply;
            BuildPreviewPanelUI.OnCanceled -= Cancel;
        }

        private void Update() {
            if (this.mode == BuildModeEnum.NONE) return;

            if (this.mode == BuildModeEnum.PAINT) {
                this.Painting();
            } else {
                this.PropsPosing();
            }
        }
        
        /**
         * This method is the entrypoint to start build mode
         */
        public void Init(PropsConfig config) {
            this.currentPropSelected = PropsManager.Instance.InstantiateProps(config, false);
            this.currentPreview = this.currentPropSelected.gameObject.AddComponent<BuildPreview>();
            
            this.SetMode(BuildModeEnum.POSING);
        }
        
        /**
         * This method is the entrypoint to start build mode
         */
        public void Init(PaintBucket paintBucket) {
            this.currentOpenedBucket = paintBucket;
            this.SetMode(BuildModeEnum.PAINT);
        }

        public BuildModeEnum GetMode() {
            return this.mode;
        }

        private void Cancel() {
            this.Reset();
            this.SetMode(BuildModeEnum.NONE);
            OnCancel?.Invoke();
        }

        private void Apply() {
            // Prevent to apply if mode is not correct or if preview is invalid
            if (!(this.mode == BuildModeEnum.VALIDATING && this.currentPreview.IsPlaceable()) && this.mode != BuildModeEnum.PAINT) {
                return;
            }

            if (this.mode == BuildModeEnum.VALIDATING) {
                OnValidatePropModification?.Invoke(this.currentPropSelected.GetConfiguration(), this.currentPropSelected.transform.position, this.currentPropSelected.transform.rotation);
                PropsManager.Instance.DestroyProps(this.currentPropSelected, false);
            } else if (this.mode == BuildModeEnum.PAINT) {
                OnValidatePaintModification?.Invoke();
            }
            
            this.SetMode(BuildModeEnum.NONE);
        }

        /**
         * This methods is used to reset buid preview to inital
         */
        private void Reset() {
            if (this.currentPropSelected) {
                // if props is selected => destroy it
                Destroy(this.currentPropSelected.gameObject);
                this.currentPropSelected = null; // TODO check to remove this
            }

            if (this.currentOpenedBucket) {
                // if a bucket was opened reset it and all walls in preview
                if (this.currentOpenedBucket.GetPaintConfig().IsWallCover()) {
                    FindObjectsOfType<Wall>().ToList().Select(x => x.GetComponent<PropsRenderer>()).ToList().ForEach(
                        propsRenderer => {
                            if (propsRenderer) {
                                propsRenderer.SetVisibilityMode(VisibilityModeEnum.AUTO);
                            }
                        });
                    FindObjectsOfType<Wall>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.Reset());
                } else if (this.currentOpenedBucket.GetPaintConfig().IsGroundCover()) {
                    FindObjectsOfType<Ground>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.ResetPreview());
                }

                this.currentOpenedBucket = null;
            }
        }

        private void ManagePropsRotation() {
            if (this.currentPropSelected.IsGroundProps()) {
                if (Input.GetKeyDown(KeyCode.DownArrow)) {
                    Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                    float newAngle = (Mathf.CeilToInt(currentLocalAngle.y / 90f) * 90f) - 90f;
                    this.currentPropSelected.transform.localEulerAngles = new Vector3(currentLocalAngle.x, newAngle, currentLocalAngle.z);
                } else if (Input.GetKeyDown(KeyCode.UpArrow)) {
                    Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                    float newAngle = (Mathf.FloorToInt(currentLocalAngle.y / 90f) * 90f) + 90f;
                    this.currentPropSelected.transform.localEulerAngles = new Vector3(currentLocalAngle.x, newAngle, currentLocalAngle.z);
                } else if (Input.GetKey(KeyCode.LeftArrow)) {
                    Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                    this.currentPropSelected.transform.localEulerAngles =
                        new Vector3(currentLocalAngle.x, currentLocalAngle.y - this.propsRotationSpeed, currentLocalAngle.z);
                } else if (Input.GetKey(KeyCode.RightArrow)) {
                    Vector3 currentLocalAngle = this.currentPropSelected.transform.localEulerAngles;
                    this.currentPropSelected.transform.localEulerAngles =
                        new Vector3(currentLocalAngle.x, currentLocalAngle.y + this.propsRotationSpeed, currentLocalAngle.z);
                }
            }
        }

        private void ManagePropMovement() {
            if (this.currentPropSelected.IsGroundProps() && hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground")) {
                float x = Mathf.FloorToInt(hit.point.x / this.propsStepSize) * this.propsStepSize;
                float z = Mathf.FloorToInt(hit.point.z / this.propsStepSize) * this.propsStepSize;
                this.currentPropSelected.transform.position = new Vector3(x, hit.point.y + (hit.normal.y * 0.01f), z);
            } else if (this.currentPropSelected.IsWallProps() && hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall")) {
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

        private int GetLayerMask() {
            if (this.mode == BuildModeEnum.POSING) {
                return CommonUtils.GetLayerMaskSurfacesToPose(this.currentPropSelected);
            }

            return (1 << 11); // Preview Layer
        }

        private void SetMode(BuildModeEnum mode) {
            Debug.Log($"Build Mode changed from {this.mode} to {mode}");
            this.mode = mode;

            if (this.mode == BuildModeEnum.NONE) {
                HUDManager.Instance.DisplayPanel(PanelTypeEnum.DEFAULT);
            } else {
                HUDManager.Instance.DisplayPanel(PanelTypeEnum.BUILD);
            }
        }

        private void PropsPosing() {
            // manage rotation of current props
            this.ManagePropsRotation();

            // Manage surface detection
            int layerMask = this.GetLayerMask();

            if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                // manage position to move current props
                if (this.mode == BuildModeEnum.POSING) {
                    this.ManagePropMovement();
                }

                // manage props follow value
                if (Input.GetMouseButtonDown(0)) {
                    if (this.mode == BuildModeEnum.POSING && this.currentPreview.IsPlaceable()) {
                        this.SetMode(BuildModeEnum.VALIDATING);

                        // TODO: Set posed props as camera target to rotate around
                        /*this.virtualCameraFollow.SetTarget(this.currentPreview.transform);
                        this.freelookCamera.enabled = true;
                        this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;*/
                    } else if (this.mode == BuildModeEnum.VALIDATING) {
                        this.SetMode(BuildModeEnum.POSING);

                        // TODO: Remove current target to allow to move with camera
                        /*this.freelookCamera.enabled = false;*/
                    }
                }
            }
        }

        private void Painting() {
            if (Input.GetMouseButtonDown(0)) {
                int layerMask = CommonUtils.GetLayerMaskSurfacesToPaint(this.currentOpenedBucket.GetPaintConfig());

                if (Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                    if (layerMask == (1 << 12)) {
                        Wall wall = hit.collider.GetComponent<Wall>();
                        wall.PreviewMaterialOnFace(hit, this.currentOpenedBucket);
                    } else if (layerMask == (1 << 9)) {
                        Ground ground = hit.collider.GetComponent<Ground>();
                        ground.Preview(this.currentOpenedBucket.GetPaintConfig());
                    }
                }
            }
        }
    }
}