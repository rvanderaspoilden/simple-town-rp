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
        private bool magnetismActivated;

        [SerializeField]
        private bool instantMagnetismActivated;

        private PropBehaviourBase currentPropBehaviour;

        private PaintBucketBehaviour currentOpenedBucket;

        private BuildModeEnum mode;

        private BuildPreview currentPreview;

        private RaycastHit hit;

        private RaycastHit magneticHit;

        private Vector3 lastPosition;

        private Vector3 lastMagneticPoint;

        private DirectionEnum magneticDirection;

        private new Camera camera;

        private ApartmentController apartmentController;

        private bool isEditing;

        private Vector3 originPosition;

        private Quaternion originRotation;

        private Vector3 currentPropsBounds;

        private Collider currentPropsCollider;

        public delegate void ValidatePropCreation(PropsConfig propsConfig, int presetId, Vector3 position, Quaternion rotation);

        public static event ValidatePropCreation OnValidatePropCreation;

        public delegate void ValidatePropEdit(PropBehaviourBase behaviour);

        public static event ValidatePropEdit OnValidatePropEdit;

        public delegate void ValidatePaintModification();

        public static event ValidatePaintModification OnValidatePaintModification;

        public delegate void MagnetismStateChanged();

        public static event MagnetismStateChanged OnMagnetismStateChange;

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
                if (Input.GetKey(KeyCode.LeftShift)) {
                    if (!this.instantMagnetismActivated) {
                        this.instantMagnetismActivated = true;
                        OnMagnetismStateChange?.Invoke();
                    }
                } else if (this.instantMagnetismActivated) {
                    this.instantMagnetismActivated = false;
                    OnMagnetismStateChange?.Invoke();
                }

                this.PropsPosing();
            }
        }

        public void Init(Delivery delivery) {
            PropsConfig propsConfig = DatabaseManager.PropsDatabase.GetPropsById(delivery.PropsConfigId);

            this.currentPropBehaviour = PropsManager.Instance.InstantiateProps(propsConfig, delivery.PropsPresetId);
            this.currentPropsCollider = this.currentPropBehaviour.GetComponent<BoxCollider>();
            this.currentPreview = this.currentPropBehaviour.gameObject.AddComponent<BuildPreview>();

            this.SetMode(BuildModeEnum.POSING);

            this.apartmentController = PlayerController.Local.CurrentGeographicArea.GetComponentInParent<ApartmentController>();

            PropsVisibilityUI.Instance.Bind(this.apartmentController);

            if (propsConfig.GetSurfaceToPose() == BuildSurfaceEnum.GROUND) {
                this.apartmentController.SetWallVisibility(VisibilityModeEnum.FORCE_HIDE);
                WallVisibilityUI.Instance.Bind(this.apartmentController, VisibilityModeEnum.FORCE_HIDE);
            } else {
                WallVisibilityUI.Instance.Bind(this.apartmentController);
            }
        }

        public void Init(PropsConfig propsConfig) {
            if (propsConfig == null) {
                Debug.LogError("[BuildManager] Init(PropsConfig) called with null config");
                return;
            }

            this.currentPropBehaviour = PropsManager.Instance.InstantiateProps(propsConfig, -1);
            this.currentPropsCollider = this.currentPropBehaviour.GetComponent<BoxCollider>();
            this.currentPreview = this.currentPropBehaviour.gameObject.AddComponent<BuildPreview>();

            this.SetMode(BuildModeEnum.POSING);

            this.apartmentController = PlayerController.Local.CurrentGeographicArea.GetComponentInParent<ApartmentController>();

            PropsVisibilityUI.Instance.Bind(this.apartmentController);

            if (propsConfig.GetSurfaceToPose() == BuildSurfaceEnum.GROUND) {
                this.apartmentController.SetWallVisibility(VisibilityModeEnum.FORCE_HIDE);
                WallVisibilityUI.Instance.Bind(this.apartmentController, VisibilityModeEnum.FORCE_HIDE);
            } else {
                WallVisibilityUI.Instance.Bind(this.apartmentController);
            }
        }

        public void Edit(PropBehaviourBase behaviour) {
            PropIdentity identity = behaviour.GetComponent<PropIdentity>();
            Debug.Log($"[BuildMode] Entering build mode prop={identity?.PropId} room={identity?.RoomId} pos={behaviour.transform.position}");

            this.currentPropBehaviour = behaviour;
            this.currentPropsCollider = this.currentPropBehaviour.GetComponent<BoxCollider>();
            this.currentPreview = this.currentPropBehaviour.gameObject.AddComponent<BuildPreview>();
            this.originPosition = this.currentPropBehaviour.transform.position;
            this.originRotation = this.currentPropBehaviour.transform.rotation;

            PropsConfig config = behaviour.GetConfiguration();
            if (config != null && config.HasPosableSurface) {
                RaycastHit[] hits = new RaycastHit[30];
                Vector3 colliderBounds = this.transform.InverseTransformDirection(this.currentPropsCollider.bounds.extents);

                var size = Physics.BoxCastNonAlloc(
                    this.originPosition + new Vector3(0, colliderBounds.y, 0),
                    colliderBounds, Vector3.up, hits, Quaternion.identity, 0.05f, (1 << 10));

                if (size > 0) {
                    foreach (var raycastHit in hits) {
                        if (raycastHit.collider && raycastHit.collider != this.currentPropsCollider) {
                            raycastHit.collider.gameObject.AddComponent<BuildPreview>();
                            raycastHit.collider.transform.parent = this.currentPropBehaviour.transform;
                        }
                    }
                }
            }

            this.isEditing = true;

            this.SetMode(BuildModeEnum.POSING);

            this.apartmentController = PlayerController.Local.CurrentGeographicArea.GetComponentInParent<ApartmentController>();

            PropsVisibilityUI.Instance.Bind(this.apartmentController);

            if (behaviour.IsGroundProps()) {
                this.apartmentController.SetWallVisibility(VisibilityModeEnum.FORCE_HIDE);
                WallVisibilityUI.Instance.Bind(this.apartmentController, VisibilityModeEnum.FORCE_HIDE);
            } else {
                WallVisibilityUI.Instance.Bind(this.apartmentController);
            }
        }

        public void Init(PaintBucketBehaviour paintBucket) {
            this.currentOpenedBucket = paintBucket;
            this.SetMode(this.currentOpenedBucket.GetPaintConfig().GetSurface() == BuildSurfaceEnum.WALL
                ? BuildModeEnum.WALL_PAINT
                : BuildModeEnum.GROUND_PAINT);

            this.apartmentController = PlayerController.Local.CurrentGeographicArea.GetComponentInParent<ApartmentController>();

            PropsVisibilityUI.Instance.Bind(this.apartmentController);
            WallVisibilityUI.Instance.Bind(this.apartmentController);
        }

        public BuildModeEnum GetMode() {
            return this.mode;
        }

        public Transform GetCurrentPreviewTransform() => currentPropBehaviour?.transform;

        public void ToggleMagnetismState() {
            this.magnetismActivated = !this.magnetismActivated;
        }

        public bool MagnetismActivated => magnetismActivated;

        public bool InstantMagnetismActivated => instantMagnetismActivated;

        public ApartmentController CurrentApartment => apartmentController;

        public void Cancel() {
            this.Reset();
            this.SetMode(BuildModeEnum.NONE);
            this.apartmentController.SetWallVisibility(VisibilityModeEnum.AUTO);
            this.apartmentController.SetPropsVisibility(VisibilityModeEnum.AUTO);
            OnCancel?.Invoke();
        }

        [Client]
        public void EditionIsValidated() {
            if (this.currentPreview) {
                this.currentPreview.Destroy();
            }

            this.isEditing = false;
        }

        private void Apply() {
            if (!(this.mode == BuildModeEnum.VALIDATING && this.currentPreview.IsPlaceable()) &&
                this.mode != BuildModeEnum.WALL_PAINT && this.mode != BuildModeEnum.GROUND_PAINT) {
                return;
            }

            if (this.mode == BuildModeEnum.VALIDATING) {
                if (this.isEditing) {
                    Debug.Log($"[BuildMode] Confirm placement requested prop={this.currentPropBehaviour?.GetComponent<PropIdentity>()?.PropId} pos={this.currentPropBehaviour?.transform.position}");
                    PropsConfig config = this.currentPropBehaviour.GetConfiguration();
                    if (config != null && config.HasPosableSurface) {
                        foreach (PropBehaviourBase child in this.currentPropBehaviour.GetComponentsInChildren<PropBehaviourBase>()) {
                            child.transform.parent = this.currentPropBehaviour.transform.parent;
                            child.GetComponent<BuildPreview>()?.Destroy();
                            OnValidatePropEdit?.Invoke(child);
                        }
                    } else {
                        OnValidatePropEdit?.Invoke(this.currentPropBehaviour);
                    }
                } else if (this.currentPropBehaviour != null) {
                    OnValidatePropCreation?.Invoke(
                        this.currentPropBehaviour.GetConfiguration(),
                        this.currentPropBehaviour.DefaultPresetId,
                        this.currentPropBehaviour.transform.position,
                        this.currentPropBehaviour.transform.rotation);
                    Destroy(this.currentPropBehaviour.gameObject);
                }
            } else if (this.mode == BuildModeEnum.WALL_PAINT || this.mode == BuildModeEnum.GROUND_PAINT) {
                OnValidatePaintModification?.Invoke();
            }

            this.apartmentController.SetWallVisibility(VisibilityModeEnum.AUTO);
            this.apartmentController.SetPropsVisibility(VisibilityModeEnum.AUTO);

            this.SetMode(BuildModeEnum.NONE);
        }

        public void Reset() {
            if (this.currentPropBehaviour) {
                if (this.isEditing) {
                    this.currentPropBehaviour.transform.position = this.originPosition;
                    this.currentPropBehaviour.transform.rotation = this.originRotation;

                    PropsConfig config = this.currentPropBehaviour.GetConfiguration();
                    if (config != null && config.HasPosableSurface) {
                        foreach (PropBehaviourBase child in this.currentPropBehaviour.GetComponentsInChildren<PropBehaviourBase>()) {
                            child.GetComponent<BuildPreview>()?.Destroy();
                            child.transform.parent = this.currentPropBehaviour.transform.parent;
                        }
                    }

                    this.currentPreview.Destroy();
                    this.currentPropBehaviour = null;
                    this.isEditing = false;
                } else {
                    Destroy(this.currentPropBehaviour.gameObject);
                    this.currentPropBehaviour = null;
                }
            }

            if (this.currentOpenedBucket != null) {
                // In client-only mode the bucket is not reparented under the apartment, so
                // GetComponentInParent<ApartmentController> would return null. Use the cached
                // apartment resolved at Init time instead.
                if (this.apartmentController != null) {
                    if (this.currentOpenedBucket.GetPaintConfig().IsWallCover()) {
                        this.apartmentController.ResetWallPreview();
                    } else if (this.currentOpenedBucket.GetPaintConfig().IsGroundCover()) {
                        this.apartmentController.ResetGroundPreview();
                    }
                }

                this.currentOpenedBucket = null;
            }
        }

        private void ManagePropsRotation() {
            if (this.currentPropBehaviour == null || !this.currentPropBehaviour.IsGroundProps()) return;

            if (Input.GetKeyDown(KeyCode.DownArrow)) {
                Vector3 a = this.currentPropBehaviour.transform.localEulerAngles;
                this.currentPropBehaviour.transform.localEulerAngles = new Vector3(a.x, (Mathf.CeilToInt(a.y / 90f) * 90f) - 90f, a.z);
            } else if (Input.GetKeyDown(KeyCode.UpArrow)) {
                Vector3 a = this.currentPropBehaviour.transform.localEulerAngles;
                this.currentPropBehaviour.transform.localEulerAngles = new Vector3(a.x, (Mathf.FloorToInt(a.y / 90f) * 90f) + 90f, a.z);
            } else if (Input.GetKey(KeyCode.LeftArrow)) {
                Vector3 a = this.currentPropBehaviour.transform.localEulerAngles;
                this.currentPropBehaviour.transform.localEulerAngles = new Vector3(a.x, a.y - this.propsRotationSpeed, a.z);
            } else if (Input.GetKey(KeyCode.RightArrow)) {
                Vector3 a = this.currentPropBehaviour.transform.localEulerAngles;
                this.currentPropBehaviour.transform.localEulerAngles = new Vector3(a.x, a.y + this.propsRotationSpeed, a.z);
            } else if (Input.GetKeyDown(KeyCode.R)) {
                Vector3 a = this.currentPropBehaviour.transform.localEulerAngles;
                this.currentPropBehaviour.transform.localEulerAngles = new Vector3(a.x, (Mathf.FloorToInt(Mathf.Round(a.y / 45f)) * 45f) + 45f, a.z);
            }
        }

        private void ManagePropMovement() {
            if (this.currentPropBehaviour != null && this.currentPropBehaviour.IsGroundProps()) {
                Vector3 point = hit.point;
                Transform t = this.currentPropBehaviour.transform;
                this.currentPropsBounds = t.InverseTransformDirection(this.currentPropsCollider.bounds.extents);

                if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground")) {
                    if (this.currentPropBehaviour.GetConfiguration().IsPosableOnProps()) {
                        if (Physics.Raycast(point, Vector3.up, out hit, 10, (1 << 16))) {
                            point = hit.point;
                        }
                    } else if (this.magnetismActivated || this.instantMagnetismActivated) {
                        float maxZ = Mathf.Abs(this.currentPropsBounds.z) + this.magneticRange;
                        float maxX = Mathf.Abs(this.currentPropsBounds.x) + this.magneticRange;

                        if (Physics.Raycast(hit.point, t.TransformDirection(Vector3.back), out magneticHit, maxZ, (1 << 12))) {
                            point = magneticHit.point;
                            this.lastMagneticPoint = point;
                            this.magneticDirection = DirectionEnum.BACK;

                            RaycastHit sub;
                            if (Physics.Raycast(magneticHit.point + magneticHit.normal * 0.001f, -t.right, out sub, maxX, (1 << 12))) {
                                point = sub.point;
                                this.lastMagneticPoint = point;
                                this.magneticDirection = DirectionEnum.LEFT;
                            } else if (Physics.Raycast(magneticHit.point + magneticHit.normal * 0.001f, t.right, out sub, maxX, (1 << 12))) {
                                point = sub.point;
                                this.lastMagneticPoint = point;
                                this.magneticDirection = DirectionEnum.RIGHT;
                            }
                        }
                    }

                    this.CalculatePlacement(point, t);
                } else if ((this.magnetismActivated || this.instantMagnetismActivated) &&
                           hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall") && hit.normal.y == 0) {
                    if (Physics.Raycast(point, Vector3.down, out magneticHit, 10, (1 << 9))) {
                        point = magneticHit.point;
                        this.lastMagneticPoint = point;
                        this.magneticDirection = DirectionEnum.DOWN;

                        float maxX = Mathf.Abs(this.currentPropsBounds.x) + this.magneticRange;
                        RaycastHit sub;
                        if (Physics.Raycast(magneticHit.point + hit.normal * 0.001f, -t.right, out sub, maxX, (1 << 12))) {
                            point = sub.point;
                            this.lastMagneticPoint = point;
                            this.magneticDirection = DirectionEnum.LEFT;
                        } else if (Physics.Raycast(magneticHit.point + hit.normal * 0.001f, t.right, out sub, maxX, (1 << 12))) {
                            point = sub.point;
                            this.lastMagneticPoint = point;
                            this.magneticDirection = DirectionEnum.RIGHT;
                        }

                        this.CalculatePlacement(point, t);
                    }
                } else if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Posable Surface")) {
                    t.position = new Vector3(hit.point.x, hit.point.y + (hit.normal.y * 0.01f), hit.point.z);
                }
            } else if (this.currentPropBehaviour != null && this.currentPropBehaviour.IsWallProps() &&
                       hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall")) {
                this.currentPropBehaviour.transform.position = hit.point + (hit.normal * 0.01f);
                Vector3 rot = this.currentPropBehaviour.transform.localEulerAngles;

                if (hit.normal == Vector3.forward)       rot.y = 360;
                else if (hit.normal == -Vector3.forward) rot.y = 180f;
                else if (hit.normal == -Vector3.left)    rot.y = 90;
                else if (hit.normal == Vector3.left)     rot.y = 270f;

                this.currentPropBehaviour.transform.eulerAngles = rot;
            }
        }

        private void CalculatePlacement(Vector3 point, Transform t) {
            float x = Mathf.FloorToInt(point.x / this.propsStepSize) * this.propsStepSize;
            float z = Mathf.FloorToInt(point.z / this.propsStepSize) * this.propsStepSize;

            if (lastPosition.x == x && lastPosition.z == z) return;

            lastPosition = new Vector3(x, 0, z);

            if ((this.magnetismActivated || this.instantMagnetismActivated) && this.lastMagneticPoint == point) {
                Vector3 offset = Vector3.zero;

                if (this.magneticDirection == DirectionEnum.BACK || this.magneticDirection == DirectionEnum.DOWN) {
                    t.position = new Vector3(x, point.y + 0.01f, point.z);
                    offset = new Vector3(0, 0, -(Mathf.Abs(this.currentPropsBounds.z) + this.magneticPropsMargin));
                } else if (this.magneticDirection == DirectionEnum.LEFT || this.magneticDirection == DirectionEnum.RIGHT) {
                    t.position = new Vector3(point.x, point.y + 0.01f, point.z);
                    int dir = this.magneticDirection == DirectionEnum.RIGHT ? 1 : -1;
                    offset = new Vector3(dir * (Mathf.Abs(this.currentPropsBounds.x) + this.magneticPropsMargin), 0,
                        -(Mathf.Abs(this.currentPropsBounds.z) + this.magneticPropsMargin));
                }

                t.position -= t.TransformDirection(offset);
                this.lastMagneticPoint = Vector3.negativeInfinity;
            } else {
                t.position = new Vector3(x, point.y + 0.01f, z);
            }
        }

        private int GetLayerMask() {
            if (this.mode == BuildModeEnum.POSING && this.currentPropBehaviour != null) {
                return CommonUtils.GetLayerMaskSurfacesToPose(this.currentPropBehaviour);
            }

            return (1 << 11);
        }

        private void SetMode(BuildModeEnum mode) {
            Debug.Log($"Build Mode changed from {this.mode} to {mode}");
            this.mode = mode;
            OnModeChanged?.Invoke(this.mode);

            if (this.mode == BuildModeEnum.NONE) {
                HUDManager.Instance.DisplayPanel(PanelTypeEnum.DEFAULT);
            } else {
                HUDManager.Instance.DisplayPanel(PanelTypeEnum.BUILD);
            }
        }

        private void PropsPosing() {
            this.ManagePropsRotation();

            int layerMask = this.GetLayerMask();

            if (!EventSystem.current.IsPointerOverGameObject() &&
                Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) {
                if (this.mode == BuildModeEnum.POSING) {
                    this.ManagePropMovement();
                }

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
            if (!Input.GetMouseButtonDown(0) || EventSystem.current.IsPointerOverGameObject()) return;

            int layerMask = CommonUtils.GetLayerMaskSurfacesToPaint(this.currentOpenedBucket.GetPaintConfig());

            if (!Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, layerMask)) return;

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
