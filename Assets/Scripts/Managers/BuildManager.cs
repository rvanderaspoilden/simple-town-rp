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

        [Header("Paint")]
        [SerializeField]
        private AudioClip paintSound;

        [SerializeField]
        [Range(0f, 1f)]
        private float paintSoundVolume = 0.6f;

        [Header("Grid placement (ground props)")]
        [SerializeField]
        [Tooltip("Cell size used when grid mode is on. Position is snapped to the nearest cell on X/Z.")]
        private float gridSize = 1f;

        [Header("Debug")]
        [SerializeField]
        private bool magnetismActivated;

        [SerializeField]
        private bool instantMagnetismActivated;

        [SerializeField]
        private bool gridModeActivated;

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

        // Hold-to-paint tracking: avoids re-acting on the same target each frame during a drag.
        private int  _lastPaintTargetId   = 0;
        private int  _lastPaintSubmesh    = -1;
        private bool _lastPaintWasErase   = false;

        // Hover preview tracking (paint mode, no mouse button held).
        private Sim.Building.Wall   _hoverWall;
        private int                 _hoverWallSubmesh = -1;
        private Sim.Building.Ground _hoverGround;

        public delegate void ValidatePropCreation(PropsConfig propsConfig, int presetId, Vector3 position, Quaternion rotation);

        public static event ValidatePropCreation OnValidatePropCreation;

        public delegate void ValidatePropEdit(PropBehaviourBase behaviour);

        public static event ValidatePropEdit OnValidatePropEdit;

        public delegate void ValidatePaintModification();

        public static event ValidatePaintModification OnValidatePaintModification;

        public delegate void MagnetismStateChanged();

        public static event MagnetismStateChanged OnMagnetismStateChange;

        public delegate void GridModeStateChanged();

        public static event GridModeStateChanged OnGridModeStateChange;

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

            this.HandleShortcuts();

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
            PropsConfig propsConfig = DatabaseManager.GetPropsById(delivery.PropsConfigId);

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

        public bool GridModeActivated => gridModeActivated;

        public float GridSize => gridSize;

        public void ToggleGridMode() {
            this.gridModeActivated = !this.gridModeActivated;
            OnGridModeStateChange?.Invoke();
        }

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

        private void HandleShortcuts() {
            if (Input.GetKeyDown(KeyCode.Escape)) {
                this.Cancel();
                return;
            }

            bool isPaint = this.mode == BuildModeEnum.WALL_PAINT || this.mode == BuildModeEnum.GROUND_PAINT;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {
                if (isPaint) {
                    this.Apply();
                } else if (this.mode == BuildModeEnum.POSING && this.currentPreview != null && this.currentPreview.IsPlaceable()) {
                    this.SetMode(BuildModeEnum.VALIDATING);
                } else if (this.mode == BuildModeEnum.VALIDATING) {
                    this.Apply();
                }
                return;
            }
        }

        public void ApplyFromInput() => Apply();

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
                    } else if (this.gridModeActivated) {
                        // Grid mode bypasses wall magnetism — placement is purely grid-snapped.
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
                } else if (!this.gridModeActivated &&
                           (this.magnetismActivated || this.instantMagnetismActivated) &&
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
            } else if (this.currentPropBehaviour != null && this.currentPropBehaviour.IsRoofProps() &&
                       hit.collider.gameObject.layer == LayerMask.NameToLayer("Roof")) {
                // Roof props (ceiling lights, etc.) snap to the underside of the roof.
                // The hit.normal points down into the room — offset slightly along it so the
                // prop doesn't z-fight with the roof surface. Rotation stays free (user can
                // tweak with R/arrows like ground props).
                this.currentPropBehaviour.transform.position = hit.point + (hit.normal * 0.01f);
            }
        }

        private void CalculatePlacement(Vector3 point, Transform t) {
            float step = this.gridModeActivated ? Mathf.Max(0.0001f, this.gridSize) : this.propsStepSize;
            // Grid mode snaps to the nearest cell centre, the fine step uses floor (smooth follow).
            float x = this.gridModeActivated
                ? Mathf.Round(point.x / step) * step
                : Mathf.FloorToInt(point.x / step) * step;
            float z = this.gridModeActivated
                ? Mathf.Round(point.z / step) * step
                : Mathf.FloorToInt(point.z / step) * step;

            if (lastPosition.x == x && lastPosition.z == z) return;

            lastPosition = new Vector3(x, 0, z);

            if (this.gridModeActivated) {
                t.position = new Vector3(x, point.y + 0.01f, z);
                return;
            }

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
            bool wasPaint = this.mode == BuildModeEnum.WALL_PAINT || this.mode == BuildModeEnum.GROUND_PAINT;
            bool isPaint  = mode      == BuildModeEnum.WALL_PAINT || mode      == BuildModeEnum.GROUND_PAINT;
            if (wasPaint && !isPaint) ClearAllHover();

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
            bool leftHeld  = Input.GetMouseButton(0);
            bool rightHeld = Input.GetMouseButton(1);

            bool overUI = EventSystem.current.IsPointerOverGameObject();
            bool isWallPaint = this.mode == BuildModeEnum.WALL_PAINT;

            // Hover preview when no paint button is held.
            if (!leftHeld && !rightHeld) {
                _lastPaintTargetId = 0;
                _lastPaintSubmesh  = -1;

                if (overUI || !TryPaintRaycast(isWallPaint, out RaycastHit hoverHit, out bool isTarget) || !isTarget) {
                    ClearAllHover();
                    return;
                }
                ApplyHoverPreview(hoverHit, isWallPaint);
                return;
            }

            ClearAllHover();
            if (overUI) return;

            if (!TryPaintRaycast(isWallPaint, out hit, out bool hitIsTarget) || !hitIsTarget) return;

            bool erase = rightHeld;

            if (isWallPaint) {
                Wall wall = hit.collider.GetComponent<Wall>();
                if (wall == null || !wall.ApartmentController.IsTenant(PlayerController.Local.CharacterData)) return;

                int submesh = wall.GetSubmeshFromHit(hit);
                if (submesh < 0) return;

                int targetId = wall.GetInstanceID();
                if (targetId == _lastPaintTargetId && submesh == _lastPaintSubmesh && erase == _lastPaintWasErase) return;

                // Idempotence: skip when the face is already in the desired state.
                bool alreadyPainted = wall.IsFacePaintedWith(submesh, this.currentOpenedBucket);
                bool alreadyOriginal = !wall.IsFacePainted(submesh);
                if ((!erase && alreadyPainted) || (erase && alreadyOriginal)) {
                    _lastPaintTargetId = targetId; _lastPaintSubmesh = submesh; _lastPaintWasErase = erase;
                    return;
                }

                _lastPaintTargetId = targetId; _lastPaintSubmesh = submesh; _lastPaintWasErase = erase;

                wall.ConsumeHover();
                if (erase) wall.ErasePaintOnFace(submesh);
                else       wall.ApplyPaintOnFace(submesh, this.currentOpenedBucket);

                PlayPaintSfx(hit.point);
            } else {
                Ground ground = hit.collider.GetComponent<Ground>();
                if (ground == null || !ground.ApartmentController.IsTenant(PlayerController.Local.CharacterData)) return;

                int targetId = ground.GetInstanceID();
                if (targetId == _lastPaintTargetId && erase == _lastPaintWasErase) return;

                // Idempotence: skip when the ground is already in the desired state.
                bool alreadyPainted  = ground.IsPaintedWith(this.currentOpenedBucket);
                bool alreadyOriginal = !ground.IsPreview();
                if ((!erase && alreadyPainted) || (erase && alreadyOriginal)) {
                    _lastPaintTargetId = targetId; _lastPaintSubmesh = 0; _lastPaintWasErase = erase;
                    return;
                }

                _lastPaintTargetId = targetId; _lastPaintSubmesh = 0; _lastPaintWasErase = erase;

                ground.ConsumeHover();
                if (erase) ground.ErasePaint();
                else       ground.ApplyPaint(this.currentOpenedBucket.GetCoverSettings());

                PlayPaintSfx(hit.point);
            }
        }

        /// <summary>
        /// Raycast for paint mode: the first non-hidden hit wins. Hidden props (FORCE_HIDE)
        /// and hidden walls (disabled collider) don't block. Returns true if the first
        /// active hit is on the requested target surface; false if blocked or nothing was hit.
        /// </summary>
        private bool TryPaintRaycast(bool isWallPaint, out RaycastHit firstActive, out bool isTarget) {
            firstActive = default;
            isTarget = false;

            int wallLayer   = 12;
            int groundLayer = 9;
            int propsLayer  = 10;
            int targetLayer = isWallPaint ? wallLayer : groundLayer;
            int mask = isWallPaint
                ? ((1 << wallLayer)  | (1 << propsLayer))
                : ((1 << groundLayer)| (1 << wallLayer) | (1 << propsLayer));

            RaycastHit[] hits = Physics.RaycastAll(this.camera.ScreenPointToRay(Input.mousePosition), 100, mask);
            if (hits.Length == 0) return false;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits) {
                if (h.collider.gameObject.layer == propsLayer) {
                    var pr = h.collider.GetComponentInParent<Sim.Building.PropsRenderer>();
                    if (pr != null && pr.IsCurrentlyHidden()) continue;
                }
                firstActive = h;
                isTarget = h.collider.gameObject.layer == targetLayer;
                return true;
            }
            return false;
        }

        private void ApplyHoverPreview(RaycastHit hoverHit, bool isWallPaint) {
            if (isWallPaint) {
                Wall wall = hoverHit.collider.GetComponent<Wall>();
                if (wall == null || !wall.ApartmentController.IsTenant(PlayerController.Local.CharacterData)) {
                    ClearAllHover();
                    return;
                }
                int submesh = wall.GetSubmeshFromHit(hoverHit);
                if (submesh < 0) { ClearAllHover(); return; }

                if (_hoverWall != wall || _hoverWallSubmesh != submesh) {
                    if (_hoverWall != null) _hoverWall.ClearHover();
                    if (_hoverGround != null) { _hoverGround.ClearHover(); _hoverGround = null; }
                    _hoverWall = wall;
                    _hoverWallSubmesh = submesh;
                    _hoverWall.HoverFace(submesh, this.currentOpenedBucket);
                }
            } else {
                Ground ground = hoverHit.collider.GetComponent<Ground>();
                if (ground == null || !ground.ApartmentController.IsTenant(PlayerController.Local.CharacterData)) {
                    ClearAllHover();
                    return;
                }

                if (_hoverGround != ground) {
                    if (_hoverGround != null) _hoverGround.ClearHover();
                    if (_hoverWall != null) { _hoverWall.ClearHover(); _hoverWall = null; _hoverWallSubmesh = -1; }
                    _hoverGround = ground;
                    _hoverGround.HoverApply(this.currentOpenedBucket.GetCoverSettings());
                }
            }
        }

        private void ClearAllHover() {
            if (_hoverWall != null) { _hoverWall.ClearHover(); _hoverWall = null; _hoverWallSubmesh = -1; }
            if (_hoverGround != null) { _hoverGround.ClearHover(); _hoverGround = null; }
        }

        private void PlayPaintSfx(Vector3 worldPos) {
            if (this.paintSound == null) return;
            AudioSource.PlayClipAtPoint(this.paintSound, worldPos, this.paintSoundVolume);
        }

        /// <summary>
        /// Apply the current bucket's paint to every editable face (walls) or every ground in the apartment.
        /// </summary>
        public void PaintAll() {
            if (this.currentOpenedBucket == null || this.apartmentController == null) return;

            if (this.mode == BuildModeEnum.WALL_PAINT) {
                foreach (Wall wall in this.apartmentController.GetComponentsInChildren<Wall>()) {
                    if (wall.ApartmentController != this.apartmentController) continue;
                    wall.ApplyPaintOnAllFaces(this.currentOpenedBucket);
                }
            } else if (this.mode == BuildModeEnum.GROUND_PAINT) {
                foreach (Ground ground in this.apartmentController.GetComponentsInChildren<Ground>()) {
                    if (ground.ApartmentController != this.apartmentController) continue;
                    ground.ApplyPaint(this.currentOpenedBucket.GetCoverSettings());
                }
            }
            PlayPaintSfx(this.apartmentController.transform.position);
        }

        /// <summary>Discards the in-progress paint preview without leaving paint mode.</summary>
        public void ResetCurrentPreview() {
            if (this.apartmentController == null) return;
            if (this.mode == BuildModeEnum.WALL_PAINT)        this.apartmentController.ResetWallPreview();
            else if (this.mode == BuildModeEnum.GROUND_PAINT) this.apartmentController.ResetGroundPreview();
        }
    }
}
