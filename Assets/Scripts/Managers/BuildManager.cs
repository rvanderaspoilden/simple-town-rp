using System;
using System.Linq;
using System.Threading;
using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using Sim.UI;
using Sim.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim {
    public class BuildManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private float propsRotationSpeed;

        [SerializeField]
        private float propsStepSize;

        [SerializeField]
        private float magneticRange;

        [SerializeField]
        private float magneticPropsMargin;

        [Header("Debug")]
        [SerializeField]
        private bool magnetic;

        private Props currentPropSelected;

        private PaintBucket currentOpenedBucket;

        private BuildModeEnum mode;

        private BuildPreview currentPreview;

        private RaycastHit hit;

        private RaycastHit magneticHit;

        private Vector3 lastPosition;

        private new Camera camera;

        // Edit properties

        private bool isEditing;

        private Vector3 originPosition;

        private Quaternion originRotation;

        private Vector3 currentPropsBounds;

        public delegate void ValidatePropCreation(PropsConfig propsConfig, Vector3 position, Quaternion rotation);

        public static event ValidatePropCreation OnValidatePropCreation;

        public delegate void ValidatePropEdit(Props props);

        public static event ValidatePropEdit OnValidatePropEdit;

        public delegate void ValidatePaintModification();

        public static event ValidatePaintModification OnValidatePaintModification;

        public delegate void CancelModification();

        public static event CancelModification OnCancel;

        public delegate void ModeChanged(BuildModeEnum mode);

        public static event ModeChanged OnModeChanged;

        public static BuildManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this);
            }

            Instance = this;
            this.camera = Camera.main;
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
            this.currentPropsBounds = this.currentPropSelected.GetComponent<Collider>().bounds.extents;
            this.currentPreview = this.currentPropSelected.gameObject.AddComponent<BuildPreview>();

            this.SetMode(BuildModeEnum.POSING);
        }

        /**
         * This method is used to start edit mode
         */
        public void Edit(Props props) {
            this.currentPropSelected = props;
            this.currentPropsBounds = this.currentPropSelected.GetComponent<Collider>().bounds.extents;
            this.currentPreview = this.currentPropSelected.gameObject.AddComponent<BuildPreview>();
            this.originPosition = this.currentPropSelected.transform.position;
            this.originRotation = this.currentPropSelected.transform.rotation;

            this.isEditing = true;

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

        public Props GetCurrentPreviewedProps() {
            return this.currentPropSelected;
        }

        private void Cancel() {
            this.Reset();
            this.SetMode(BuildModeEnum.NONE);
            RoomManager.Instance.SetWallVisibility(VisibilityModeEnum.AUTO);
            RoomManager.Instance.SetPropsVisibility(VisibilityModeEnum.AUTO);
            OnCancel?.Invoke();
        }

        private void Apply() {
            // Prevent to apply if mode is not correct or if preview is invalid
            if (!(this.mode == BuildModeEnum.VALIDATING && this.currentPreview.IsPlaceable()) && this.mode != BuildModeEnum.PAINT) {
                return;
            }

            if (this.mode == BuildModeEnum.VALIDATING) {
                if (this.isEditing) {
                    OnValidatePropEdit?.Invoke(this.currentPropSelected);
                    this.currentPreview.Destroy();
                    this.isEditing = false;
                } else {
                    OnValidatePropCreation?.Invoke(this.currentPropSelected.GetConfiguration(), this.currentPropSelected.transform.position,
                        this.currentPropSelected.transform.rotation);
                    PropsManager.Instance.DestroyProps(this.currentPropSelected, false);
                }
            } else if (this.mode == BuildModeEnum.PAINT) {
                OnValidatePaintModification?.Invoke();
            }

            RoomManager.Instance.SetWallVisibility(VisibilityModeEnum.AUTO);
            RoomManager.Instance.SetPropsVisibility(VisibilityModeEnum.AUTO);

            this.SetMode(BuildModeEnum.NONE);
        }

        /**
         * This methods is used to reset buid preview to inital
         */
        private void Reset() {
            if (this.currentPropSelected) {
                if (this.isEditing) {
                    this.currentPropSelected.transform.position = this.originPosition;
                    this.currentPropSelected.transform.rotation = this.originRotation;
                    this.currentPreview.Destroy();
                    this.currentPropSelected = null;
                    this.isEditing = false;
                } else {
                    // if props is selected => destroy it
                    Destroy(this.currentPropSelected.gameObject);
                    this.currentPropSelected = null;
                }
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
            if (this.currentPropSelected.IsGroundProps()) {
                if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground")) {
                    Vector3 point = hit.point;
                    Transform currentPropsTransform = this.currentPropSelected.transform;

                    //Manage magnetic movement
                    Vector3 magneticDir = currentPropsTransform.TransformDirection(Vector3.back);
                    
                    if (this.magnetic && Physics.Raycast(point, magneticDir, out magneticHit, this.magneticRange, (1 << 12))) {
                        point = magneticHit.point;
                    }

                    float x = Mathf.FloorToInt(point.x / this.propsStepSize) * this.propsStepSize;
                    float z = Mathf.FloorToInt(point.z / this.propsStepSize) * this.propsStepSize;

                    if (lastPosition.x != x || lastPosition.z != z) {
                        lastPosition = new Vector3(x, 0, z);
                        
                        if (magnetic && point == magneticHit.point) {
                            Debug.Log("-----------------------");    
                            /*Debug.Log(this.currentPropSelected.transform.position);
                            Debug.Log(this.currentPropsBounds.x);
                            Debug.Log(this.currentPropsBounds.x + this.magneticPropsMargin);*/

                            Vector3 relativeBounds =
                                this.currentPropSelected.transform.InverseTransformDirection(this.currentPropSelected.GetComponent<Collider>().bounds.extents);
                            
                            Debug.Log(relativeBounds);
                            Debug.Log(this.currentPropsBounds);
                            Debug.Log(this.currentPropSelected.GetComponent<Collider>().bounds.extents);
                            
                            this.currentPropSelected.transform.position = new Vector3(x, hit.point.y + (hit.normal.y * 0.01f), point.z);

                            this.currentPropSelected.transform.position -=
                                this.currentPropSelected.transform.TransformDirection(new Vector3(0, 0, -(Mathf.Abs(relativeBounds.z) + this.magneticPropsMargin)));
                        } else {
                            this.currentPropSelected.transform.position = new Vector3(x, hit.point.y + (hit.normal.y * 0.01f), z);
                        }
                    }
                } else if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall")) {
                    Vector3 point = hit.point;
                    Transform currentPropsTransform = this.currentPropSelected.transform;

                    Debug.DrawRay(point, Vector3.down * 10, Color.red);
                    if (this.magnetic && Physics.Raycast(point, Vector3.down, out magneticHit, 10, (1 << 9))) {
                        point = magneticHit.point;
                        Debug.Log(point);
                    }

                    float x = Mathf.FloorToInt(point.x / this.propsStepSize) * this.propsStepSize;
                    float z = Mathf.FloorToInt(point.z / this.propsStepSize) * this.propsStepSize;

                    if (lastPosition.x != x || lastPosition.z != z) {
                        lastPosition = new Vector3(x, 0, z);

                        this.currentPropSelected.transform.position = new Vector3(x, point.y + (magneticHit.normal.y * 0.01f), z);

                        if (magnetic && point == magneticHit.point) {
                            this.currentPropSelected.transform.position -=
                                this.currentPropSelected.transform.TransformDirection(new Vector3(0, 0, -(this.currentPropsBounds.x + this.magneticPropsMargin)));
                        }
                    }
                } else if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Posable Surface")) {
                    this.currentPropSelected.transform.position = new Vector3(hit.point.x, hit.point.y + (hit.normal.y * 0.01f), hit.point.z);
                }
            } else if (this.currentPropSelected.IsWallProps() && hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall")) {
                this.currentPropSelected.transform.position = hit.point + (hit.normal * 0.01f);
                Vector3 rotation = this.currentPropSelected.transform.localEulerAngles;

                if (hit.normal == Vector3.forward) {
                    rotation.y = 360;
                } else if (hit.normal == -Vector3.forward) {
                    rotation.y = 180f;
                } else if (hit.normal == -Vector3.left) {
                    rotation.y = 90;
                } else if (hit.normal == Vector3.left) {
                    rotation.y = 270f;
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
            OnModeChanged?.Invoke(this.mode);

            // TODO: put this out of this class
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

            if (!EventSystem.current.IsPointerOverGameObject() && Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                // manage position to move current props
                if (this.mode == BuildModeEnum.POSING) {
                    this.ManagePropMovement();
                }

                // manage props follow value
                if (Input.GetMouseButtonDown(0)) {
                    if (this.mode == BuildModeEnum.POSING && this.currentPreview.IsPlaceable()) {
                        this.SetMode(BuildModeEnum.VALIDATING);
                    } else if (this.mode == BuildModeEnum.VALIDATING) {
                        this.SetMode(BuildModeEnum.POSING);
                    }
                }
            }
        }

        private void Painting() {
            if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject()) {
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