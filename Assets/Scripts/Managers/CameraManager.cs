using AI.States;
using Interaction;
using Sim.Enums;
using Sim.Interactables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private LayerMask layerMaskInFreeMode;

        [SerializeField]
        private LayerMask defaultCullingMask;
        
        [SerializeField]
        private LayerMask fpsCullingMask;
        
        [SerializeField]
        private GameObject fpsCamera;

        [Header("Run on double click")]
        [Tooltip("Fenêtre temporelle entre deux mouseDown gauche pour qu'ils soient considérés comme un double clic et déclenchent la course.")]
        [SerializeField] private float doubleClickWindow = 0.3f;

        private float _lastLeftDownTime = -10f;
        private bool _pendingRunRequest;

        private BuildCamera buildCamera;
        
        private ThirdPersonCamera tpsCamera;

        private float lastCameraPosition;

        private RaycastHit hit;

        private CameraModeEnum currentMode;

        private float mouseClickTimer;

        private new Camera camera;

        private bool startLeftClickValid;

        public static CameraManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }


            this.camera = GetComponentInChildren<Camera>();
            this.buildCamera = GetComponent<BuildCamera>();
            this.tpsCamera = GetComponent<ThirdPersonCamera>();

            this.buildCamera.enabled = false;
            this.tpsCamera.enabled = false;
            this.fpsCamera.SetActive(false);

            DontDestroyOnLoad(this.gameObject);
        }

        private void Start() {
            PlayerController.OnStateChanged += OnStateChanged;
        }

        private void OnDestroy() {
            PlayerController.OnStateChanged -= OnStateChanged;
        }

        public Camera Camera => camera;

        public UniversalAdditionalCameraData GetCameraData() {
            return this.camera.GetUniversalAdditionalCameraData();
        }

        public void SetCameraTarget(Transform target) {
            this.tpsCamera.SetCameraTarget(target);
        }

        void Update() {
            if (PlayerController.Local == null || PlayerController.Local.PlayerState == PlayerState.DIED) return;

            if (this.currentMode == CameraModeEnum.FREE) {
                this.ManageInteraction();
                this.ManageHover();
            } else if (this._hoveredOutline != null) {
                this._hoveredOutline.Hide();
                this._hoveredOutline = null;
            }
        }

        private PropHoverOutline _hoveredOutline;
        private int _outlineLayerBit = -1;

        /// <summary>
        /// Interaction raycast mask, plus the "Outline" layer bit. While a prop is
        /// hovered its renderers sit on the Outline layer, so we must keep that layer
        /// hittable or the hover/click would flicker as the prop changes layer.
        /// </summary>
        private LayerMask InteractionMask {
            get {
                if (_outlineLayerBit < 0) {
                    int l = LayerMask.NameToLayer(PropHoverOutline.OutlineLayerName);
                    _outlineLayerBit = l >= 0 ? (1 << l) : 0;
                }
                return this.layerMaskInFreeMode | _outlineLayerBit;
            }
        }

        /// <summary>
        /// Per-frame hover highlight: raycasts under the cursor and outlines the
        /// IInteractable being pointed at (toggling the outline as the hovered target
        /// changes). Works for any IInteractable — props, doors, NPCs, items, job
        /// boards… — not just PropBehaviourBase.
        /// </summary>
        private void ManageHover() {
            PropHoverOutline target = null;

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUI) {
                Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit h, 100, this.InteractionMask, QueryTriggerInteraction.Ignore)) {
                    IInteractable interactable = h.collider.GetComponentInParent<IInteractable>();
                    if (interactable != null && interactable.IsInteractable()) {
                        GameObject host = interactable.transform.gameObject;
                        target = host.GetComponent<PropHoverOutline>();
                        if (target == null) target = host.AddComponent<PropHoverOutline>();
                    }
                }
            }

            if (target != this._hoveredOutline) {
                if (this._hoveredOutline != null) this._hoveredOutline.Hide();
                this._hoveredOutline = target;
                if (this._hoveredOutline != null) this._hoveredOutline.Show();
            }
        }

        public CameraModeEnum GetMode() {
            return this.currentMode;
        }

        private void OnStateChanged(PlayerController player, StateType state) {
            if (player.isLocalPlayer) {
                if (state == StateType.FREE) {
                    this.SetCurrentMode(CameraModeEnum.FREE);
                } else {
                    this.SetCurrentMode(CameraModeEnum.BUILD);
                }
            }
        }

        public void SetFpsCamera(Transform point) {
            this.fpsCamera.transform.position = point.position;
            this.fpsCamera.transform.rotation = point.rotation;
            this.SetCurrentMode(CameraModeEnum.FPS);
        }

        // The Outline layer must always be in the camera culling mask so that props
        // moved to that layer by PropHoverOutline.Show() remain visible while outlined.
        private LayerMask WithOutlineLayer(LayerMask mask) {
            int l = LayerMask.NameToLayer(PropHoverOutline.OutlineLayerName);
            if (l >= 0) mask |= (1 << l);
            return mask;
        }

        public void SetCurrentMode(CameraModeEnum mode) {
            this.currentMode = mode;
            this.buildCamera.enabled = mode == CameraModeEnum.BUILD;
            this.tpsCamera.enabled = mode == CameraModeEnum.FREE;
            this.fpsCamera.SetActive(mode == CameraModeEnum.FPS);

            if (this.currentMode == CameraModeEnum.BUILD) {
                this.buildCamera.Setup(this.tpsCamera.GetVirtualCamera());
                this.camera.cullingMask = this.WithOutlineLayer(this.defaultCullingMask);
            } else if (this.currentMode == CameraModeEnum.FREE) {
                this.tpsCamera.Setup(this.buildCamera.GetVirtualCamera());
                this.camera.cullingMask = this.WithOutlineLayer(this.defaultCullingMask);
            } else {
                this.camera.cullingMask = this.WithOutlineLayer(this.fpsCullingMask);
            }
        }

        private void ManageInteraction() {
            if (SubGameController.IsActive) return;

            bool leftMouseClick = Input.GetMouseButtonUp(0);
            bool leftMousePressed = Input.GetMouseButton(0);
            bool rightMouseClick = Input.GetMouseButtonUp(1);

            if (Input.GetMouseButtonDown(0)) {
                this.startLeftClickValid = !EventSystem.current.IsPointerOverGameObject();
                if (this.startLeftClickValid) {
                    // Détection double clic : le 2e mouseDown dans la fenêtre arme
                    // _pendingRunRequest. Un simple clic le remet à false — le flag
                    // se réarme proprement à chaque nouveau cycle de clic.
                    bool isDouble = (Time.unscaledTime - _lastLeftDownTime) <= doubleClickWindow;
                    _pendingRunRequest = isDouble;
                    _lastLeftDownTime = Time.unscaledTime;
                }
            }

            if (((leftMouseClick || leftMousePressed) && !this.startLeftClickValid) || EventSystem.current.IsPointerOverGameObject()) {
                return;
            }

            Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);

            if (!(leftMouseClick || rightMouseClick || leftMousePressed)) return;

            // Ignore trigger colliders: props can host triggers for occupancy detection (e.g. DoorPropSource)
            // and we don't want those to intercept the click — only solid geometry should be hit.
            RaycastHit[] hits = Physics.RaycastAll(ray.origin, ray.direction, 100, this.InteractionMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            bool useLeftClickFilter = leftMouseClick || leftMousePressed;
            bool hasHit = false;
            foreach (RaycastHit h in hits) {
                if (useLeftClickFilter) {
                    IInteractable ii = h.collider.GetComponentInParent<IInteractable>();
                    if (ii != null && ii.IsRightClickOnly()) continue;
                }
                hit = h;
                hasHit = true;
                break;
            }
            if (!hasHit) return;

            {
                IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
                PlayerController player = hit.collider.GetComponent<PlayerController>();

                if (interactable != null && !leftMousePressed) {
                    if (interactable.IsInteractable()) {
                        bool canInteract = PlayerController.Local.CanInteractWith(interactable, hit.point);
                        Action[] actions = interactable.GetActions();

                        if (leftMouseClick && (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove) ||
                                               PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle))) {
                            actions = interactable.GetActions(true);

                            canInteract = canInteract || (actions.Length == 1 && actions[0].Type.Equals(ActionTypeEnum.LOOK));
                        }

                        if (canInteract) {
                            if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove)) {
                                PlayerController.Local.Idle();
                            } else if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle)) {
                                PlayerController.Local.LookAt(interactable.transform);
                            }

                            HUDManager.Instance.ShowContextMenu(actions, interactable.transform, leftMouseClick);
                        } else {
                            PlayerController.Local.SetTarget(hit.point, interactable, leftMouseClick);
                        }
                    } else {
                        PlayerController.Local.SetTarget(hit.point, interactable);
                    }
                } else if (interactable == null && leftMousePressed && hit.collider.gameObject.layer.Equals(LayerMask.NameToLayer("Ground"))) {
                    PlayerController.Local.MoveTo(hit.point, _pendingRunRequest);
                } else if (rightMouseClick && player) {
                    if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove) ||
                        PlayerController.Local.CurrentState().GetType() == typeof(CharacterInteract)) {
                        PlayerController.Local.Idle();
                    }

                    if (player == PlayerController.Local) {
                        HUDManager.Instance.CloseContextMenu();
                        HUDManager.Instance.ToggleInventory();
                    } else if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle)) {
                        PlayerController.Local.LookAt(player.transform);
                        HUDManager.Instance.ShowContextMenu(player.Actions, player.transform);
                    }
                }
            }
        }
    }
}