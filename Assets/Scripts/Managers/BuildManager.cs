using Mirror;
using Sim.Building;
using Sim.Entities;
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

        private Vector3 lastMagneticPoint;

        private DirectionEnum magneticDirection;

        private new Camera camera;

        private ApartmentController apartmentController;

        // Edit properties

        private bool isEditing;

        private Vector3 originPosition;

        private Quaternion originRotation;

        private Vector3 currentPropsBounds;

        private Collider currentPropsCollider;

        public delegate void ValidatePropCreation(PropsConfig propsConfig, int presetId, Vector3 position, Quaternion rotation);

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
            if (Instance != null && Instance != this) {
                Destroy(this);
            } else {
                Instance = this;
            }

            this.camera = GetComponentInChildren<Camera>();
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
            if (this.mode == BuildModeEnum.NONE)
                return;

            if (this.mode == BuildModeEnum.WALL_PAINT || this.mode == BuildModeEnum.GROUND_PAINT) {
                this.Painting();
            } else {
                this.PropsPosing();
            }
        }

        public void Init(Delivery delivery) {
            PropsConfig propsConfig = DatabaseManager.PropsDatabase.GetPropsById(delivery.PropsConfigId);

            this.currentPropSelected = PropsManager.Instance.InstantiateProps(propsConfig, delivery.PropsPresetId);
            this.currentPropsCollider = this.currentPropSelected.GetComponent<BoxCollider>();
            this.currentPreview = this.currentPropSelected.gameObject.AddComponent<BuildPreview>();

            this.SetMode(BuildModeEnum.POSING);

            this.apartmentController = PlayerController.Local.CurrentGeographicArea.GetComponentInParent<ApartmentController>();

            PropsVisibilityUI.Instance.Bind(this.apartmentController);
            WallVisibilityUI.Instance.Bind(this.apartmentController);
        }

        /**
         * This method is used to start edit mode
         */
        public void Edit(Props props) {
            this.currentPropSelected = props;
            this.currentPropsCollider = this.currentPropSelected.GetComponent<BoxCollider>();
            this.currentPreview = this.currentPropSelected.gameObject.AddComponent<BuildPreview>();
            this.originPosition = this.currentPropSelected.transform.position;
            this.originRotation = this.currentPropSelected.transform.rotation;

            this.isEditing = true;

            this.SetMode(BuildModeEnum.POSING);

            this.apartmentController = PlayerController.Local.CurrentGeographicArea.GetComponentInParent<ApartmentController>();

            PropsVisibilityUI.Instance.Bind(this.apartmentController);
            WallVisibilityUI.Instance.Bind(this.apartmentController);
        }

        /**
         * This method is the entrypoint to start build mode
         */
        public void Init(PaintBucket paintBucket) {
            this.currentOpenedBucket = paintBucket;
            this.SetMode(this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.WALL ? BuildModeEnum.WALL_PAINT : BuildModeEnum.GROUND_PAINT);

            this.apartmentController = PlayerController.Local.CurrentGeographicArea.GetComponentInParent<ApartmentController>();

            PropsVisibilityUI.Instance.Bind(this.apartmentController);
            WallVisibilityUI.Instance.Bind(this.apartmentController);
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
            this.apartmentController.SetWallVisibility(VisibilityModeEnum.AUTO);
            this.apartmentController.SetPropsVisibility(VisibilityModeEnum.AUTO);
            OnCancel?.Invoke();
        }

        [Client]
        public void EditionIsValidated() {
            this.currentPreview.Destroy();
            this.isEditing = false;
        }

        private void Apply() {
            // Prevent to apply if mode is not correct or if preview is invalid
            if (!(this.mode == BuildModeEnum.VALIDATING && this.currentPreview.IsPlaceable()) && this.mode != BuildModeEnum.WALL_PAINT && this.mode != BuildModeEnum.GROUND_PAINT) {
                return;
            }

            if (this.mode == BuildModeEnum.VALIDATING) {
                if (this.isEditing) {
                    OnValidatePropEdit?.Invoke(this.currentPropSelected);
                } else {
                    OnValidatePropCreation?.Invoke(this.currentPropSelected.GetConfiguration(),
                        this.currentPropSelected.PresetId,
                        this.currentPropSelected.transform.position,
                        this.currentPropSelected.transform.rotation);
                    Destroy(this.currentPropSelected.gameObject);
                }
            } else if (this.mode == BuildModeEnum.WALL_PAINT || this.mode == BuildModeEnum.GROUND_PAINT) {
                OnValidatePaintModification?.Invoke();
            }

            this.apartmentController.SetWallVisibility(VisibilityModeEnum.AUTO);
            this.apartmentController.SetPropsVisibility(VisibilityModeEnum.AUTO);

            this.SetMode(BuildModeEnum.NONE);
        }

        /**
         * This methods is used to reset buid preview to inital
         */
        public void Reset() {
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
                    this.currentOpenedBucket.GetComponentInParent<ApartmentController>().ResetWallPreview(); // TODO: use apartmentController
                } else if (this.currentOpenedBucket.GetPaintConfig().IsGroundCover()) {
                    this.currentOpenedBucket.GetComponentInParent<ApartmentController>().ResetGroundPreview();
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
                Vector3 point = hit.point;
                Transform currentPropsTransform = this.currentPropSelected.transform;
                this.currentPropsBounds = currentPropsTransform.InverseTransformDirection(this.currentPropsCollider.bounds.extents);

                if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground")) {
                    if (this.currentPropSelected.GetConfiguration().IsPosableOnProps()) {
                        if (Physics.Raycast(point, Vector3.up, out hit, 10, (1 << 16))) {
                            point = hit.point;
                        }
                    } else if (this.magnetic) {
                        float maxHitDistanceZ = Mathf.Abs(this.currentPropsBounds.z) + this.magneticRange;
                        float maxHitDistanceX = Mathf.Abs(this.currentPropsBounds.x) + this.magneticRange;

                        if (Physics.Raycast(hit.point, currentPropsTransform.TransformDirection(Vector3.back), out magneticHit, maxHitDistanceZ, (1 << 12))) {
                            point = magneticHit.point;
                            this.lastMagneticPoint = point;
                            this.magneticDirection = DirectionEnum.BACK;

                            RaycastHit subHit;
                            if (Physics.Raycast(magneticHit.point + (magneticHit.normal * 0.001f), -currentPropsTransform.right, out subHit, maxHitDistanceX,
                                (1 << 12))) {
                                point = subHit.point;
                                this.lastMagneticPoint = point;
                                this.magneticDirection = DirectionEnum.LEFT;
                            } else if (Physics.Raycast(magneticHit.point + (magneticHit.normal * 0.001f), currentPropsTransform.right, out subHit, maxHitDistanceX,
                                (1 << 12))) {
                                point = subHit.point;
                                this.lastMagneticPoint = point;
                                this.magneticDirection = DirectionEnum.RIGHT;
                            }
                        }
                    }

                    this.CalculatePlacement(point, currentPropsTransform);
                } else if (this.magnetic && hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall") && hit.normal.y == 0) {
                    if (Physics.Raycast(point, Vector3.down, out magneticHit, 10, (1 << 9))) {
                        point = magneticHit.point;
                        this.lastMagneticPoint = point;
                        this.magneticDirection = DirectionEnum.DOWN;

                        float maxHitDistanceX = Mathf.Abs(this.currentPropsBounds.x) + this.magneticRange;
                        RaycastHit subHit;
                        if (Physics.Raycast(magneticHit.point + (hit.normal * 0.001f), -currentPropsTransform.right, out subHit, maxHitDistanceX, (1 << 12))) {
                            point = subHit.point;
                            this.lastMagneticPoint = point;
                            this.magneticDirection = DirectionEnum.LEFT;
                        } else if (Physics.Raycast(magneticHit.point + (hit.normal * 0.001f), currentPropsTransform.right, out subHit, maxHitDistanceX, (1 << 12))) {
                            point = subHit.point;
                            this.lastMagneticPoint = point;
                            this.magneticDirection = DirectionEnum.RIGHT;
                        }

                        this.CalculatePlacement(point, currentPropsTransform);
                    }
                } else if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Posable Surface")) {
                    currentPropsTransform.position = new Vector3(hit.point.x, hit.point.y + (hit.normal.y * 0.01f), hit.point.z);
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

        private void CalculatePlacement(Vector3 point, Transform currentPropsTransform) {
            float x = Mathf.FloorToInt(point.x / this.propsStepSize) * this.propsStepSize;
            float z = Mathf.FloorToInt(point.z / this.propsStepSize) * this.propsStepSize;

            // Update position only if a change needed
            if (lastPosition.x != x || lastPosition.z != z) {
                lastPosition = new Vector3(x, 0, z);

                if (magnetic && this.lastMagneticPoint == point) {
                    Vector3 offset = Vector3.zero;

                    if (this.magneticDirection == DirectionEnum.BACK || this.magneticDirection == DirectionEnum.DOWN) {
                        currentPropsTransform.position = new Vector3(x, point.y + 0.01f, point.z);
                        offset = new Vector3(0, 0, -(Mathf.Abs(this.currentPropsBounds.z) + this.magneticPropsMargin));
                    } else if (this.magneticDirection == DirectionEnum.LEFT || this.magneticDirection == DirectionEnum.RIGHT) {
                        currentPropsTransform.position = new Vector3(point.x, point.y + 0.01f, point.z);

                        int direction = this.magneticDirection == DirectionEnum.RIGHT ? 1 : -1;
                        offset = new Vector3(direction * (Mathf.Abs(this.currentPropsBounds.x) + this.magneticPropsMargin), 0,
                            -(Mathf.Abs(this.currentPropsBounds.z) + this.magneticPropsMargin));
                    }

                    currentPropsTransform.position -= currentPropsTransform.TransformDirection(offset);

                    this.lastMagneticPoint = Vector3.negativeInfinity;
                } else {
                    currentPropsTransform.position = new Vector3(x, point.y + 0.01f, z);
                }
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

                        if (wall.ApartmentController.IsTenant(PlayerController.Local.CharacterData)) {
                            wall.PreviewMaterialOnFace(hit, this.currentOpenedBucket);
                        }
                    } else if (layerMask == (1 << 9)) {
                        Ground ground = hit.collider.GetComponent<Ground>();

                        if (ground.ApartmentController.IsTenant(PlayerController.Local.CharacterData)) {
                            ground.Preview(this.currentOpenedBucket.GetCoverSettings());
                        }
                    }
                }
            }
        }
    }
}